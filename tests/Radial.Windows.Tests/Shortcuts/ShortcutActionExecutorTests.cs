using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Tests.Shortcuts;

public class ShortcutActionExecutorTests
{
    private sealed class FakeClock : IClock
    {
        public TimeSpan Now { get; set; }
    }

    private sealed class FakeInjector : IKeyboardInjector
    {
        public List<IReadOnlyList<KeyboardEvent>> Sent { get; } = [];

        public KeyInjectionResult Result { get; set; } = new(true, 4);

        public Action? OnSend { get; set; }

        public KeyInjectionResult Send(IReadOnlyList<KeyboardEvent> events)
        {
            OnSend?.Invoke();
            Sent.Add(events);
            return Result;
        }
    }

    /// <summary>Estado físico de los modificadores guiado por un guion: cada lectura consume un valor; el último se repite.</summary>
    private sealed class ScriptedModifiers(params KeyModifiers[] script) : IModifierStateReader
    {
        private int _index;

        public int Reads { get; private set; }

        public KeyModifiers ReadPressed()
        {
            Reads++;
            var value = script[Math.Min(_index, script.Length - 1)];
            _index++;
            return value;
        }
    }

    private readonly FakeClock _clock = new();
    private readonly FakeInjector _injector = new();
    private readonly List<TimeSpan> _pauses = [];
    private IntPtr _foreground = new(1234);

    private ShortcutActionExecutor Create(IModifierStateReader? modifiers = null, ShortcutExecutorOptions? options = null, Func<TimeSpan, CancellationToken, Task>? pause = null) =>
        new(
            _injector,
            modifiers ?? new ScriptedModifiers(KeyModifiers.None),
            _clock,
            () => _foreground,
            options,
            pause ?? ((interval, token) =>
            {
                _pauses.Add(interval);
                _clock.Now += interval;
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }));

    private static SendKeysAction Keys(KeyModifiers modifiers, string key) => new(new KeyChord(modifiers, key));

    private static string Describe(IEnumerable<KeyboardEvent> events) => string.Join(" ", events.Select(e => $"{(e.KeyUp ? '-' : '+')}{e.Code:X2}"));

    // Ejecución correcta

    [Fact]
    public async Task ACombination_IsSentAsOneSequence()
    {
        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.True(result.IsSuccess);
        Assert.Equal("+A2 +43 -43 -A2", Describe(Assert.Single(_injector.Sent)));
    }

    [Theory]
    [InlineData(KeyModifiers.Control, "C", "+A2 +43 -43 -A2")]
    [InlineData(KeyModifiers.Control, "V", "+A2 +56 -56 -A2")]
    [InlineData(KeyModifiers.Control, "Z", "+A2 +5A -5A -A2")]
    [InlineData(KeyModifiers.Control, "A", "+A2 +41 -41 -A2")]
    [InlineData(KeyModifiers.Control, "S", "+A2 +53 -53 -A2")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, "S", "+A2 +A0 +53 -53 -A0 -A2")]
    [InlineData(KeyModifiers.Alt, "Tab", "+A4 +09 -09 -A4")]
    [InlineData(KeyModifiers.Windows, "E", "+5B +45 -45 -5B")]
    public async Task DifferentCombinations_AreJustDifferentData_ForTheSameExecutor(KeyModifiers modifiers, string key, string expected)
    {
        var result = await Create().ExecuteAsync(Keys(modifiers, key));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Describe(Assert.Single(_injector.Sent)));
    }

    [Fact]
    public async Task TheKeyIsCaseInsensitive_AsInTheVocabulary()
    {
        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "c"));

        Assert.True(result.IsSuccess);
        Assert.Equal("+A2 +43 -43 -A2", Describe(Assert.Single(_injector.Sent)));
    }

    [Fact]
    public void OnlyShortcutsAreSupported()
    {
        var executor = Create();

        Assert.True(executor.CanExecute(Keys(KeyModifiers.Control, "C")));
        Assert.False(executor.CanExecute(new OpenUrlAction("https://example.com")));
        Assert.False(executor.CanExecute(new OpenMenuAction(new MenuId("x"))));
    }

    // Espera a la liberación de los modificadores físicos

    [Fact]
    public async Task WithNoModifiersPressed_ItSendsAtOnce_WithoutWaiting()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.None);

        await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Empty(_pauses);
        Assert.Single(_injector.Sent);
    }

    [Fact]
    public async Task WithCtrlStillHeld_ItWaitsUntilItIsReleased_AndOnlyThenSends()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.Control, KeyModifiers.Control, KeyModifiers.None);
        var sentAfterReads = -1;
        _injector.OnSend = () => sentAfterReads = modifiers.Reads;

        var result = await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _pauses.Count);
        Assert.Equal(4, sentAfterReads); // se envió justo después de leer «ninguno»
        Assert.Single(_injector.Sent);
    }

    [Theory]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Windows)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift)]
    public async Task AnyPhysicalModifierMakesItWait_NotOnlyTheOnesTheShortcutUses(KeyModifiers held)
    {
        var modifiers = new ScriptedModifiers(held, KeyModifiers.None);

        var result = await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.True(result.IsSuccess);
        Assert.Single(_pauses);
    }

    [Fact]
    public async Task TheWait_PollsAtTheConfiguredInterval()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.None);
        var options = new ShortcutExecutorOptions { PollInterval = TimeSpan.FromMilliseconds(25) };

        await Create(modifiers, options).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal([TimeSpan.FromMilliseconds(25)], _pauses);
    }

    [Fact]
    public async Task IfTheModifiersAreNeverReleased_ItTimesOut_AfterTheDefaultFiveSeconds_AndSendsNothing()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.Control | KeyModifiers.Shift);

        var result = await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.ModifiersNotReleased, result.Error!.Code);
        Assert.Contains("Ctrl", result.Error.Message);
        Assert.Contains("Mayús", result.Error.Message);
        Assert.Contains("5", result.Error.Message);
        Assert.Empty(_injector.Sent);
        Assert.InRange(_clock.Now, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5.1));
    }

    [Fact]
    public async Task TheTimeoutIsConfigurable()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.Alt);
        var options = new ShortcutExecutorOptions { ModifierReleaseTimeout = TimeSpan.FromSeconds(1) };

        var result = await Create(modifiers, options).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.ModifiersNotReleased, result.Error!.Code);
        Assert.InRange(_clock.Now, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.1));
        Assert.Contains("Alt", result.Error.Message);
    }

    [Fact]
    public async Task AReleaseJustBeforeTheDeadline_StillSends()
    {
        // Libera en la lectura 499; el plazo (5 s con consulta cada 10 ms) vence en la 500.
        var script = Enumerable.Repeat(KeyModifiers.Control, 498).Append(KeyModifiers.None).ToArray();

        var result = await Create(new ScriptedModifiers(script)).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TheExecutor_NeverPressesOrReleasesThePhysicalModifiers_WhileWaiting()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.Control, KeyModifiers.None);
        var readsWhenSent = 0;
        _injector.OnSend = () => readsWhenSent = modifiers.Reads;

        await Create(modifiers, pause: (_, _) => Task.CompletedTask).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        // Durante la espera no se inyectó nada (ni un Ctrl «de ayuda» ni un «soltar Ctrl»): solo la secuencia del atajo, una vez,
        // y después de ver los modificadores libres.
        Assert.Equal(3, readsWhenSent);
        Assert.Equal("+A2 +43 -43 -A2", Describe(Assert.Single(_injector.Sent)));
    }

    // Cancelación

    [Fact]
    public async Task ATokenAlreadyCancelled_DoesNothing()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.None);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, "C"), cts.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(0, modifiers.Reads);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task CancellingWhileWaiting_ReturnsCancelled_AndSendsNothing()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var executor = Create(
            new ScriptedModifiers(KeyModifiers.Control),
            pause: (_, token) =>
            {
                if (++calls == 3)
                {
                    cts.Cancel();
                }

                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        var result = await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"), cts.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Equal(3, calls);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task CancellingExactlyWhenTheModifiersAreReleased_StillSendsNothing()
    {
        using var cts = new CancellationTokenSource();
        var executor = Create(
            new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.None),
            pause: (_, _) =>
            {
                cts.Cancel(); // la pausa termina con normalidad, pero el token ya está cancelado
                return Task.CompletedTask;
            });

        var result = await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"), cts.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Empty(_injector.Sent);
    }

    // Acciones no válidas

    [Fact]
    public async Task AnotherTypeOfAction_IsUnsupported_AndNothingIsRead()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.None);

        var result = await Create(modifiers).ExecuteAsync(new OpenUrlAction("https://example.com"));

        Assert.Equal(ActionErrorCodes.UnsupportedAction, result.Error!.Code);
        Assert.Equal(0, modifiers.Reads);
        Assert.Empty(_injector.Sent);
    }

    [Theory]
    [InlineData("Ñ")]
    [InlineData("Esc")]
    [InlineData("CapsLock")]
    [InlineData("Ctrl")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" C")]
    [InlineData("F25")]
    public async Task AKeyOutsideTheVocabulary_IsInvalid_AndNothingIsSent(string key)
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.None);

        var result = await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, key));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Equal(0, modifiers.Reads); // ni siquiera espera: la validación va primero
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task UnknownModifierBits_AreInvalid()
    {
        var result = await Create().ExecuteAsync(Keys((KeyModifiers)64, "C"));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task ANullChord_IsInvalid()
    {
        var result = await Create().ExecuteAsync(new SendKeysAction(null!));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task TheMessageOfAnInvalidAction_DoesNotRepeatTheTextTheUserTyped()
    {
        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "Contraseña-secreta"));

        Assert.DoesNotContain("Contraseña", result.Error!.Message, StringComparison.Ordinal);
    }

    // La ventana en primer plano

    [Fact]
    public async Task IfTheForegroundWindowChangesWhileWaiting_NothingIsSent()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.None);
        var executor = Create(
            modifiers,
            pause: (_, _) =>
            {
                _foreground = new IntPtr(999); // el usuario cambió de ventana mientras esperaba
                return Task.CompletedTask;
            });

        var result = await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.ForegroundChanged, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task WithTheSameForegroundWindow_ItSends()
    {
        var result = await Create(new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.None)).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TheExecutor_OnlyReadsTheForeground_NeverChangesIt()
    {
        // No recibe ninguna operación para cambiar el foco: el atajo llega a la ventana que ya estaba activa.
        var reads = 0;
        var executor = new ShortcutActionExecutor(_injector, new ScriptedModifiers(KeyModifiers.None), _clock, () =>
        {
            reads++;
            return _foreground;
        });

        await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.True(reads >= 2); // antes de esperar y justo antes de enviar
    }

    [Fact]
    public async Task WithNoForegroundWindowAtTheStart_NothingIsReadOrSent()
    {
        _foreground = IntPtr.Zero;
        var modifiers = new ScriptedModifiers(KeyModifiers.None);

        var result = await Create(modifiers).ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.ForegroundChanged, result.Error!.Code);
        Assert.Empty(_injector.Sent);
        Assert.Equal(0, modifiers.Reads); // ni siquiera se espera: no hay a quién enviarle el atajo
    }

    [Fact]
    public async Task IfTheForegroundWindowDisappearsWhileWaiting_NothingIsSent()
    {
        var executor = Create(
            new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.None),
            pause: (_, _) =>
            {
                _foreground = IntPtr.Zero; // la ventana activa se cerró y aún no hay otra
                return Task.CompletedTask;
            });

        var result = await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.ForegroundChanged, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task TheTarget_IsTheForegroundWindowWhenTheExecutionStarts_NotOneReadLater()
    {
        // Se lee antes de la primera consulta de modificadores: es la ventana que tenía el foco al confirmar.
        var order = new List<string>();
        var modifiers = new ScriptedModifiers(KeyModifiers.None);
        var executor = new ShortcutActionExecutor(
            _injector,
            new RecordingModifiers(order, modifiers),
            _clock,
            () =>
            {
                order.Add("foreground");
                return _foreground;
            });

        await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(["foreground", "modifiers", "foreground"], order);
    }

    private sealed class RecordingModifiers(List<string> order, IModifierStateReader inner) : IModifierStateReader
    {
        public KeyModifiers ReadPressed()
        {
            order.Add("modifiers");
            return inner.ReadPressed();
        }
    }

    // Fallos de inyección

    [Fact]
    public async Task IfTheReleaseOfWhatWasLeftPressedCannotBeConfirmed_ItIsReportedWithItsOwnCode()
    {
        _injector.Result = new KeyInjectionResult(false, 2, 5, ReleaseFailed: true);

        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.KeyReleaseFailed, result.Error!.Code);
        Assert.Contains("pulsada", result.Error.Message);
    }

    [Fact]
    public async Task AFailureWhoseReleaseWasConfirmed_IsAPlainInjectionFailure()
    {
        _injector.Result = new KeyInjectionResult(false, 2, 5, ReleaseFailed: false);

        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.InputInjectionFailed, result.Error!.Code);
    }

    [Fact]
    public async Task AnUnexpectedInjectorException_PropagatesToTheCoordinator_NeverAsASuccess()
    {
        _injector.OnSend = () => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().ExecuteAsync(Keys(KeyModifiers.Control, "C")));
    }

    [Fact]
    public async Task IfTheSystemCallThrew_AndTheReleaseWasConfirmed_ItIsAnInjectionFailure_NotASuccess()
    {
        _injector.OnSend = () => throw new KeyInjectionException("Falló.", new InvalidOperationException("secreto-interno"), releaseConfirmed: true);

        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.InputInjectionFailed, result.Error!.Code);
        Assert.Contains("InvalidOperationException", result.Error.Message);
        Assert.DoesNotContain("secreto-interno", result.Error.Message);
    }

    [Fact]
    public async Task IfTheSystemCallThrew_AndTheReleaseWasNotConfirmed_TheUserIsWarnedAboutStuckKeys()
    {
        _injector.OnSend = () => throw new KeyInjectionException("Falló.", new InvalidOperationException("x"), releaseConfirmed: false);

        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.KeyReleaseFailed, result.Error!.Code);
        Assert.Contains("pulsada", result.Error.Message);
    }

    [Fact]
    public async Task IfItIsCancelledAfterTheForegroundCheck_NothingIsSent()
    {
        // La cancelación (nuevo gesto, aislamiento, cierre) puede llegar entre la comprobación del foco y el envío.
        using var cancel = new CancellationTokenSource();
        var reads = 0;
        var executor = new ShortcutActionExecutor(
            _injector,
            new ScriptedModifiers(KeyModifiers.None),
            _clock,
            () =>
            {
                if (++reads == 2)
                {
                    cancel.Cancel(); // justo después de la segunda lectura, la previa al envío
                }

                return _foreground;
            });

        var result = await executor.ExecuteAsync(Keys(KeyModifiers.Control, "C"), cancel.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task IfWindowsRejectsTheInput_TheFailureIsReturned_WithTheErrorCode()
    {
        _injector.Result = new KeyInjectionResult(false, 1, 5);

        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal(ActionErrorCodes.InputInjectionFailed, result.Error!.Code);
        Assert.Contains("5", result.Error.Message);
    }

    [Fact]
    public async Task AFailureWithoutAnErrorCode_HasAMessageWithoutANumber()
    {
        _injector.Result = new KeyInjectionResult(false, 0);

        var result = await Create().ExecuteAsync(Keys(KeyModifiers.Control, "C"));

        Assert.Equal(ActionErrorCodes.InputInjectionFailed, result.Error!.Code);
        Assert.DoesNotContain("error 0", result.Error.Message);
    }

    // Construcción

    [Fact]
    public void InvalidOptions_AreRejected()
    {
        var modifiers = new ScriptedModifiers(KeyModifiers.None);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ShortcutActionExecutor(_injector, modifiers, _clock, () => _foreground, new ShortcutExecutorOptions { ModifierReleaseTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShortcutActionExecutor(_injector, modifiers, _clock, () => _foreground, new ShortcutExecutorOptions { PollInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentNullException>(() => new ShortcutActionExecutor(null!, modifiers, _clock, () => _foreground));
        Assert.Throws<ArgumentNullException>(() => new ShortcutActionExecutor(_injector, null!, _clock, () => _foreground));
        Assert.Throws<ArgumentNullException>(() => new ShortcutActionExecutor(_injector, modifiers, null!, () => _foreground));
        Assert.Throws<ArgumentNullException>(() => new ShortcutActionExecutor(_injector, modifiers, _clock, null!));
    }

    [Fact]
    public void TheProductionExecutor_CanBeCreated_WithoutSendingAnything()
    {
        var executor = ShortcutActionExecutor.CreateDefault();

        Assert.True(executor.CanExecute(Keys(KeyModifiers.Control, "C")));
    }

    [Fact]
    public async Task ANullAction_IsAProgrammingError() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Create().ExecuteAsync(null!));
}
