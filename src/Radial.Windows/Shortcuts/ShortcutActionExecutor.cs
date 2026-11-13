using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Native;

namespace Radial.Windows.Shortcuts;

/// <summary>Parámetros del ejecutor de atajos.</summary>
public sealed record ShortcutExecutorOptions
{
    /// <summary>Cuánto se espera a que se suelten los modificadores físicos (documento técnico 3.4: 5 s, configurable).</summary>
    public TimeSpan ModifierReleaseTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Cada cuánto se consulta el estado de los modificadores durante la espera.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(10);
}

/// <summary>
/// Ejecutor de TODOS los atajos de teclado (<see cref="SendKeysAction"/>): Ctrl+C, Ctrl+Mayús+S, Alt+Tab, Win+E... son datos de la
/// acción, no clases. Orden fijo:
/// 1. Otro tipo de acción: <see cref="ActionErrorCodes.UnsupportedAction"/>. Token ya cancelado: <see cref="ActionExecutionResult.Cancelled"/>.
/// 2. <c>GetProblems()</c> de Core no vacío (o tecla sin traducción): <see cref="ActionErrorCodes.InvalidAction"/>; no se envía nada.
/// 3. Espera a que se suelten TODOS los modificadores físicos (el Ctrl del gesto y cualquier otro; documento técnico 3.4). Sin
///    tocarlos: no se pulsa ni se suelta nada artificialmente. Si vence: <see cref="ActionErrorCodes.ModifiersNotReleased"/> y no
///    se emite ninguna tecla. Cancelable.
/// 4. Si no hay ventana en primer plano, o cambió durante la espera: <see cref="ActionErrorCodes.ForegroundChanged"/> (el atajo
///    iba para la aplicación que había al confirmar, no para otra). El destino se toma al EMPEZAR la ejecución, con el overlay ya
///    oculto y sin haberse activado nunca, y se vuelve a comprobar justo antes de enviar.
/// 5. Una sola secuencia de entrada con <see cref="IKeyboardInjector"/>; si el sistema la rechaza,
///    <see cref="ActionErrorCodes.InputInjectionFailed"/>, o <see cref="ActionErrorCodes.KeyReleaseFailed"/> si además no se pudo
///    confirmar la liberación de lo que quedó pulsado. El éxito significa «Windows aceptó la secuencia», no «la aplicación la
///    procesó» (una aplicación elevada puede descartarla sin que se sepa: UIPI).
/// No cambia el foco (la ventana del overlay no se activa), no usa <c>cmd</c>, PowerShell ni procesos y no toca los hooks de
/// captura. Cuando se envían las teclas ya no se puede cancelar.
/// </summary>
public sealed class ShortcutActionExecutor : IActionExecutor
{
    private readonly IKeyboardInjector _injector;
    private readonly ModifierReleaseWaiter _waiter;
    private readonly Func<IntPtr> _foregroundWindow;

    public ShortcutActionExecutor(
        IKeyboardInjector injector,
        IModifierStateReader modifiers,
        IClock clock,
        Func<IntPtr> foregroundWindow,
        ShortcutExecutorOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(foregroundWindow);

        var effective = options ?? new ShortcutExecutorOptions();
        if (effective.ModifierReleaseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "El tiempo de espera debe ser positivo.");
        }

        if (effective.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "El intervalo de consulta debe ser positivo.");
        }

        _injector = injector;
        _waiter = new ModifierReleaseWaiter(modifiers, clock, effective, pause ?? Task.Delay);
        _foregroundWindow = foregroundWindow;
    }

    /// <summary>Ejecutor de producción: <c>SendInput</c>, <c>GetAsyncKeyState</c>, reloj monotónico y ventana en primer plano de Windows.</summary>
    public static ShortcutActionExecutor CreateDefault(ShortcutExecutorOptions? options = null) =>
        new(new SendInputKeyboardInjector(), new Win32ModifierStateReader(), new MonotonicClock(), NativeMethods.GetForegroundWindow, options);

    public bool CanExecute(RadialAction action) => action is SendKeysAction;

    public async Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not SendKeysAction keys)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.UnsupportedAction, $"Este ejecutor no admite acciones de tipo {action.GetType().Name}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        var problems = keys.GetProblems().ToList();
        if (problems.Count > 0)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.InvalidAction, "La acción no es válida: " + string.Join(" ", problems));
        }

        // El vocabulario es único (Core), así que una acción válida siempre se traduce; esto protege de una tabla incompleta.
        if (!ShortcutParser.TryParse(keys.Chord, out var shortcut))
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.InvalidAction, "La combinación de teclas no se puede traducir a teclas de Windows.");
        }

        // El destino es la ventana con el foco ahora: el overlay no se activa, así que sigue siendo la aplicación que había debajo.
        // Sin ventana en primer plano (transición de foco, escritorio seguro) el teclado no iría a ninguna aplicación.
        var target = _foregroundWindow();
        if (target == IntPtr.Zero)
        {
            return NoForegroundWindow();
        }

        var wait = await _waiter.WaitAsync("No se envió el atajo.", cancellationToken).ConfigureAwait(false);
        if (wait is not null)
        {
            return wait;
        }

        var current = _foregroundWindow();
        if (current == IntPtr.Zero)
        {
            return NoForegroundWindow();
        }

        if (current != target)
        {
            return ActionExecutionResult.Failure(
                ActionErrorCodes.ForegroundChanged,
                "La ventana activa cambió mientras se esperaba a soltar las teclas; no se envió el atajo.");
        }

        // Último punto en que se puede cancelar: una vez enviada, la secuencia no se puede recoger.
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        // Éxito = Windows ACEPTÓ la secuencia. No garantiza que la aplicación la procese (p. ej. una aplicación elevada, UIPI).
        KeyInjectionResult sent;
        try
        {
            sent = _injector.Send(ShortcutSequence.Build(shortcut));
        }
        catch (KeyInjectionException exception)
        {
            // La llamada al sistema lanzó: no se sabe cuántas teclas entraron. Nunca se informa de éxito; el mensaje no repite
            // el texto de la excepción original, solo su tipo.
            var inner = exception.InnerException?.GetType().Name ?? "desconocida";
            return exception.ReleaseConfirmed
                ? ActionExecutionResult.Failure(ActionErrorCodes.InputInjectionFailed, $"Falló el envío del atajo ({inner}); se soltaron las teclas que pudieran haber quedado pulsadas.")
                : ActionExecutionResult.Failure(
                    ActionErrorCodes.KeyReleaseFailed,
                    $"Falló el envío del atajo ({inner}) y no se pudo confirmar que se soltaran las teclas: puede que alguna siga pulsada. Pulsa y suelta Ctrl, Mayús, Alt y Win en el teclado para comprobarlo.");
        }

        if (sent.Success)
        {
            return ActionExecutionResult.Success;
        }

        if (sent.ReleaseFailed)
        {
            return ActionExecutionResult.Failure(
                ActionErrorCodes.KeyReleaseFailed,
                "Windows no aceptó el atajo y no se pudo confirmar que se soltaran las teclas enviadas: puede que alguna siga pulsada. " +
                "Pulsa y suelta Ctrl, Mayús, Alt y Win en el teclado para comprobarlo.");
        }

        return ActionExecutionResult.Failure(
            ActionErrorCodes.InputInjectionFailed,
            sent.Win32Error == 0
                ? "Windows no aceptó el envío del atajo."
                : $"Windows no aceptó el envío del atajo (error {sent.Win32Error}).");
    }

    private static ActionExecutionResult NoForegroundWindow() =>
        ActionExecutionResult.Failure(ActionErrorCodes.ForegroundChanged, "No hay ninguna ventana en primer plano; no se envió el atajo.");
}
