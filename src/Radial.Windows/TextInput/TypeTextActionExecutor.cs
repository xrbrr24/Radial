using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Native;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.TextInput;

/// <summary>
/// Escribe un texto (<see cref="TypeTextAction"/>) en la aplicación que tiene el foco, como entrada Unicode y sin tocar el
/// portapapeles. Orden fijo, igual que el de los atajos:
/// 1. Otro tipo de acción: <see cref="ActionErrorCodes.UnsupportedAction"/>. Token ya cancelado: <see cref="ActionExecutionResult.Cancelled"/>.
/// 2. <c>GetProblems()</c> de Core no vacío: <see cref="ActionErrorCodes.InvalidAction"/>; no se envía nada.
/// 3. Sin ventana en primer plano: <see cref="ActionErrorCodes.ForegroundChanged"/>.
/// 4. Espera a que se suelten los modificadores físicos (el Ctrl del gesto): si no, cada carácter llegaría como Ctrl+carácter.
///    Si vence: <see cref="ActionErrorCodes.ModifiersNotReleased"/>. Cancelable.
/// 5. Si la ventana en primer plano cambió durante la espera: <see cref="ActionErrorCodes.ForegroundChanged"/> (el texto iba para
///    otra aplicación).
/// 6. UNA llamada de envío con todo el texto (Windows no intercala entrada ajena dentro de ella); si el sistema no la acepta entera,
///    se suelta lo pulsado y se informa: <see cref="ActionErrorCodes.InputInjectionFailed"/> o <see cref="ActionErrorCodes.KeyReleaseFailed"/>.
/// El éxito es «Windows aceptó la entrada», no «la aplicación la procesó» (una aplicación elevada la descarta sin avisar: UIPI).
/// Los mensajes NUNCA repiten el texto (puede ser una contraseña o datos personales); el registro tampoco. Se usa el inyector de
/// producción, separado de la autoprueba: sus eventos no llevan la marca de Radial y los hooks los descartan por inyectados.
/// </summary>
public sealed class TypeTextActionExecutor : IActionExecutor
{
    private readonly IKeyboardInjector _injector;
    private readonly ModifierReleaseWaiter _waiter;
    private readonly Func<IntPtr> _foregroundWindow;

    public TypeTextActionExecutor(
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
    public static TypeTextActionExecutor CreateDefault(ShortcutExecutorOptions? options = null) =>
        new(new SendInputKeyboardInjector(), new Win32ModifierStateReader(), new MonotonicClock(), NativeMethods.GetForegroundWindow, options);

    public bool CanExecute(RadialAction action) => action is TypeTextAction;

    public async Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not TypeTextAction typeText)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.UnsupportedAction, $"Este ejecutor no admite acciones de tipo {action.GetType().Name}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        var problems = typeText.GetProblems().ToList();
        if (problems.Count > 0)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.InvalidAction, "La acción no es válida: " + string.Join(" ", problems));
        }

        var events = TextSequence.Build(typeText.Text);

        var target = _foregroundWindow();
        if (target == IntPtr.Zero)
        {
            return NoForegroundWindow();
        }

        var wait = await _waiter.WaitAsync("No se escribió el texto.", cancellationToken).ConfigureAwait(false);
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
                "La ventana activa cambió mientras se esperaba a soltar las teclas; no se escribió el texto.");
        }

        // Último punto en que se puede cancelar: una vez enviado, no se puede recoger.
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        KeyInjectionResult sent;
        try
        {
            sent = _injector.Send(events);
        }
        catch (KeyInjectionException exception)
        {
            var inner = exception.InnerException?.GetType().Name ?? "desconocida";
            return exception.ReleaseConfirmed
                ? ActionExecutionResult.Failure(ActionErrorCodes.InputInjectionFailed, $"Falló el envío del texto ({inner}); se soltaron las teclas que pudieran haber quedado pulsadas.")
                : ActionExecutionResult.Failure(
                    ActionErrorCodes.KeyReleaseFailed,
                    $"Falló el envío del texto ({inner}) y no se pudo confirmar que se soltaran las teclas: puede que alguna siga pulsada. Pulsa y suelta Ctrl, Mayús, Alt y Win en el teclado para comprobarlo.");
        }

        if (sent.Success)
        {
            return ActionExecutionResult.Success;
        }

        if (sent.ReleaseFailed)
        {
            return ActionExecutionResult.Failure(
                ActionErrorCodes.KeyReleaseFailed,
                "Windows no aceptó el texto y no se pudo confirmar que se soltaran las teclas enviadas: puede que alguna siga pulsada. " +
                "Pulsa y suelta Ctrl, Mayús, Alt y Win en el teclado para comprobarlo.");
        }

        // Puede haberse escrito una parte del texto: se dice, sin repetirlo.
        return ActionExecutionResult.Failure(
            ActionErrorCodes.InputInjectionFailed,
            sent.Win32Error == 0
                ? "Windows no aceptó el envío del texto; puede que se haya escrito solo una parte."
                : $"Windows no aceptó el envío del texto (error {sent.Win32Error}); puede que se haya escrito solo una parte.");
    }

    private static ActionExecutionResult NoForegroundWindow() =>
        ActionExecutionResult.Failure(ActionErrorCodes.ForegroundChanged, "No hay ninguna ventana en primer plano; no se escribió el texto.");
}
