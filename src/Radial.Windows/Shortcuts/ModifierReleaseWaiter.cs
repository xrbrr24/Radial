using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;

namespace Radial.Windows.Shortcuts;

/// <summary>
/// Espera a que se suelten los modificadores FÍSICOS (Ctrl, Mayús, Alt, Win) antes de enviar teclado: el Ctrl del gesto sigue pulsado
/// al confirmar y, con él, cualquier tecla enviada se convertiría en una combinación distinta de la deseada (documento técnico 3.4).
/// No pulsa ni suelta nada artificialmente. La usan los ejecutores que envían teclado (atajos y texto).
/// </summary>
internal sealed class ModifierReleaseWaiter
{
    private readonly IModifierStateReader _modifiers;
    private readonly IClock _clock;
    private readonly ShortcutExecutorOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;

    public ModifierReleaseWaiter(
        IModifierStateReader modifiers,
        IClock clock,
        ShortcutExecutorOptions options,
        Func<TimeSpan, CancellationToken, Task> pause)
    {
        _modifiers = modifiers;
        _clock = clock;
        _options = options;
        _pause = pause;
    }

    /// <param name="notSentText">Qué se dice que no se envió si vence: «No se envió el atajo.», «No se escribió el texto.»…</param>
    /// <returns><c>null</c> si ya no hay modificadores pulsados; si no, el resultado con el que termina la ejecución.</returns>
    public async Task<ActionExecutionResult?> WaitAsync(string notSentText, CancellationToken cancellationToken)
    {
        var deadline = _clock.Now + _options.ModifierReleaseTimeout;
        KeyModifiers pressed;

        while ((pressed = _modifiers.ReadPressed()) != KeyModifiers.None)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ActionExecutionResult.Cancelled;
            }

            if (_clock.Now >= deadline)
            {
                return ActionExecutionResult.Failure(
                    ActionErrorCodes.ModifiersNotReleased,
                    $"Siguen pulsadas estas teclas tras {_options.ModifierReleaseTimeout.TotalSeconds:0.#} s: {Describe(pressed)}. {notSentText}");
            }

            try
            {
                await _pause(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ActionExecutionResult.Cancelled;
            }
        }

        // Soltadas a tiempo; si se canceló justo entonces, no se envía nada.
        return cancellationToken.IsCancellationRequested ? ActionExecutionResult.Cancelled : null;
    }

    private static string Describe(KeyModifiers modifiers)
    {
        var names = new List<string>(4);
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            names.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            names.Add("Mayús");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            names.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Windows))
        {
            names.Add("Win");
        }

        return string.Join(", ", names);
    }
}
