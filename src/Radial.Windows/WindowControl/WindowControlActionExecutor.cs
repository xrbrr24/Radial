using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.WindowControl;

/// <summary>
/// Minimiza, maximiza, restaura o cierra la ventana EXTERNA que tiene el foco (<see cref="WindowControlAction"/>). Orden fijo:
/// 1. Otro tipo de acción: <see cref="ActionErrorCodes.UnsupportedAction"/>. Token ya cancelado: <see cref="ActionExecutionResult.Cancelled"/>.
/// 2. <c>GetProblems()</c> de Core no vacío: <see cref="ActionErrorCodes.InvalidAction"/>.
/// 3. El destino es la ventana con el foco al empezar (el overlay no se activa, así que sigue siendo la que había debajo). Si no
///    hay ninguna, no existe, es de Radial (centro de control u overlay: nunca se actúa sobre ellas), es el escritorio o la barra
///    de tareas, o no es visible: <see cref="ActionErrorCodes.WindowNotAvailable"/>.
/// 4. Si no admite la operación (sin botón de minimizar o maximizar, ya minimizada, ya maximizada, ya en tamaño normal al
///    restaurar): <see cref="ActionErrorCodes.WindowOperationNotSupported"/>.
/// 5. Se vuelve a comprobar que la ventana sigue siendo válida justo antes de actuar y se encola la orden sin esperar a que la
///    ventana responda; si Windows la rechaza (p. ej. proceso elevado, UIPI): <see cref="ActionErrorCodes.WindowOperationFailed"/>.
/// Todo el trabajo va en el pool: quien llama es el hilo de interfaz y no se bloquea. El éxito significa «Windows aceptó la orden»,
/// no «la aplicación la atendió» (una aplicación puede preguntar si guardar antes de cerrar). Los mensajes no incluyen títulos
/// de ventana ni nombres de programa.
/// </summary>
public sealed class WindowControlActionExecutor : IActionExecutor
{
    private readonly IWindowSystem _windows;

    public WindowControlActionExecutor(IWindowSystem windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows;
    }

    /// <summary>Ejecutor de producción sobre user32.</summary>
    public static WindowControlActionExecutor CreateDefault() => new(new Win32WindowSystem());

    public bool CanExecute(RadialAction action) => action is WindowControlAction;

    public async Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not WindowControlAction window)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.UnsupportedAction, $"Este ejecutor no admite acciones de tipo {action.GetType().Name}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        var problems = window.GetProblems().ToList();
        if (problems.Count > 0)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.InvalidAction, "La acción no es válida: " + string.Join(" ", problems));
        }

        try
        {
            // Con el token cancelado antes de empezar, Task.Run no ejecuta nada. Una vez dentro, la orden se encola y se informa.
            return await Task.Run(() => Perform(window.Operation), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }
    }

    private ActionExecutionResult Perform(WindowOperation operation)
    {
        var target = _windows.GetForegroundWindow();
        if (target == IntPtr.Zero)
        {
            return Unavailable("No hay ninguna ventana en primer plano.");
        }

        var snapshot = _windows.Inspect(target);
        if (Reject(snapshot) is { } rejected)
        {
            return rejected;
        }

        if (NotSupported(operation, snapshot) is { } reason)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.WindowOperationNotSupported, reason);
        }

        // La ventana pudo cerrarse o cambiar mientras se comprobaba: última comprobación justo antes de actuar.
        if (Reject(_windows.Inspect(target)) is { } changed)
        {
            return changed;
        }

        var result = _windows.Execute(target, operation);
        if (result.Success)
        {
            return ActionExecutionResult.Success;
        }

        return ActionExecutionResult.Failure(
            ActionErrorCodes.WindowOperationFailed,
            result.Win32Error == 5
                ? "Windows rechazó la operación: la ventana pertenece a un programa con más privilegios (ejecutado como administrador)."
                : result.Win32Error == 0
                    ? "Windows no aceptó la operación sobre la ventana."
                    : $"Windows no aceptó la operación sobre la ventana (error {result.Win32Error}).");
    }

    private static ActionExecutionResult? Reject(WindowSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            return Unavailable("La ventana ya no existe.");
        }

        if (snapshot.IsOwnWindow)
        {
            return Unavailable("La ventana activa es de Radial; no se actúa sobre las ventanas de Radial.");
        }

        if (snapshot.IsShell)
        {
            return Unavailable("El escritorio y la barra de tareas no se pueden controlar.");
        }

        return snapshot.IsVisible ? null : Unavailable("La ventana activa no es visible.");
    }

    private static string? NotSupported(WindowOperation operation, WindowSnapshot snapshot) => operation switch
    {
        WindowOperation.Minimize when snapshot.State == WindowShowState.Minimized => "La ventana ya está minimizada.",
        WindowOperation.Minimize when !snapshot.CanMinimize => "Esta ventana no se puede minimizar.",
        WindowOperation.Maximize when snapshot.State == WindowShowState.Maximized => "La ventana ya está maximizada.",
        WindowOperation.Maximize when !snapshot.CanMaximize => "Esta ventana no se puede maximizar.",
        WindowOperation.Restore when snapshot.State == WindowShowState.Normal => "La ventana ya tiene su tamaño normal.",
        _ => null,
    };

    private static ActionExecutionResult Unavailable(string message) =>
        ActionExecutionResult.Failure(ActionErrorCodes.WindowNotAvailable, message);
}
