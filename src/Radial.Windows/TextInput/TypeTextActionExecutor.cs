using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Native;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.TextInput;

/// <summary>Ritmo con el que se escribe un texto: cuántos caracteres van en cada envío y cuánto se espera entre envíos.</summary>
public sealed record TypeTextOptions
{
    /// <summary>
    /// Caracteres por llamada a <c>SendInput</c> (un emoji, un salto de línea o un tabulador cuentan como uno). Un texto de
    /// cientos de eventos de una sola vez llega a la cola de la aplicación de golpe; algunas aplicaciones (editores con servicios de
    /// texto, como el Bloc de notas de Windows 11) lo procesan de forma asíncrona y pueden mezclar o duplicar lo que reciben.
    /// </summary>
    public int CharactersPerBatch { get; init; } = 4;

    /// <summary>Espera entre dos envíos. Windows la redondea al reloj del sistema (unos 15 ms).</summary>
    public TimeSpan PauseBetweenBatches { get; init; } = TimeSpan.FromMilliseconds(5);
}

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
/// 6. El texto se envía en lotes de pocos caracteres COMPLETOS (<see cref="TypeTextOptions"/>), cada uno en UNA llamada (Windows no
///    intercala entrada ajena dentro de ella) y con una pausa entre lotes. Entre lotes se vuelve a comprobar que siguen sin estar
///    pulsados los modificadores y que la ventana en primer plano es la misma: si no, se detiene (el resto del texto NO se escribe
///    en otro sitio ni como atajos). Si el sistema no acepta un lote entero, se suelta lo pulsado y se informa:
///    <see cref="ActionErrorCodes.InputInjectionFailed"/> o <see cref="ActionErrorCodes.KeyReleaseFailed"/>. Ningún lote deja una
///    tecla pulsada al acabar.
/// El éxito es «Windows aceptó la entrada», no «la aplicación la procesó» (una aplicación elevada la descarta sin avisar: UIPI).
/// Los mensajes NUNCA repiten el texto (puede ser una contraseña o datos personales); el registro tampoco. Se usa el inyector de
/// producción, separado de la autoprueba: sus eventos no llevan la marca de Radial y los hooks los descartan por inyectados.
/// </summary>
public sealed class TypeTextActionExecutor : IActionExecutor
{
    private readonly IKeyboardInjector _injector;
    private readonly ModifierReleaseWaiter _waiter;
    private readonly IModifierStateReader _modifiers;
    private readonly Func<IntPtr> _foregroundWindow;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;
    private readonly TypeTextOptions _textOptions;

    public TypeTextActionExecutor(
        IKeyboardInjector injector,
        IModifierStateReader modifiers,
        IClock clock,
        Func<IntPtr> foregroundWindow,
        ShortcutExecutorOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? pause = null,
        TypeTextOptions? textOptions = null)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(foregroundWindow);

        _textOptions = textOptions ?? new TypeTextOptions();
        if (_textOptions.CharactersPerBatch < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(textOptions), "Debe haber al menos un carácter por envío.");
        }

        if (_textOptions.PauseBetweenBatches < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(textOptions), "La pausa entre envíos no puede ser negativa.");
        }

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
        _modifiers = modifiers;
        _pause = pause ?? Task.Delay;
        _waiter = new ModifierReleaseWaiter(modifiers, clock, effective, _pause);
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

        var batches = TextSequence.BuildBatches(typeText.Text, _textOptions.CharactersPerBatch);

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

        // Último punto en que se puede cancelar sin haber escrito nada: una vez enviado un lote, no se puede recoger.
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        for (var index = 0; index < batches.Count; index++)
        {
            if (index > 0)
            {
                // Entre lotes: la aplicación procesa lo recibido y se comprueba que nada ha cambiado. Lo que falta del texto NO se
                // escribe si el usuario ya está en otra ventana o ha vuelto a pulsar un modificador (saldrían atajos, no texto).
                try
                {
                    await _pause(_textOptions.PauseBetweenBatches, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return ActionExecutionResult.Cancelled;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return ActionExecutionResult.Cancelled;
                }

                var now = _foregroundWindow();
                if (now == IntPtr.Zero || now != target)
                {
                    return ActionExecutionResult.Failure(
                        ActionErrorCodes.ForegroundChanged,
                        "La ventana activa cambió mientras se escribía el texto; se detuvo y solo se escribió una parte.");
                }

                var pressed = _modifiers.ReadPressed();
                if (pressed != KeyModifiers.None)
                {
                    return ActionExecutionResult.Failure(
                        ActionErrorCodes.ModifiersNotReleased,
                        "Se pulsó Ctrl, Mayús, Alt o Win mientras se escribía el texto; se detuvo y solo se escribió una parte.");
                }
            }

            var failure = SendBatch(batches[index]);
            if (failure is not null)
            {
                return failure;
            }
        }

        return ActionExecutionResult.Success;
    }

    /// <returns><c>null</c> si Windows aceptó el lote entero; si no, el resultado con el que termina la ejecución.</returns>
    private ActionExecutionResult? SendBatch(IReadOnlyList<KeyboardEvent> batch)
    {
        KeyInjectionResult sent;
        try
        {
            sent = _injector.Send(batch);
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
            return null;
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
