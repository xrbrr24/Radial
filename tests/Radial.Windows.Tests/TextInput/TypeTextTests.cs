using Radial.Core.Execution;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Native;
using Radial.Windows.Shortcuts;
using Radial.Windows.TextInput;

namespace Radial.Windows.Tests.TextInput;

/// <summary>Insertar texto: la secuencia de eventos y el ejecutor, con dobles. Nunca se envía entrada real.</summary>
public class TypeTextTests
{
    private sealed class FakeClock : IClock
    {
        public TimeSpan Now { get; set; }
    }

    private sealed class FakeInjector : IKeyboardInjector
    {
        public List<IReadOnlyList<KeyboardEvent>> Sent { get; } = [];

        public KeyInjectionResult Result { get; set; } = new(true, 0);

        public Exception? Throw { get; set; }

        public KeyInjectionResult Send(IReadOnlyList<KeyboardEvent> events)
        {
            Sent.Add(events);
            return Throw is { } exception
                ? throw exception
                : Result;
        }
    }

    private sealed class ScriptedModifiers(params KeyModifiers[] script) : IModifierStateReader
    {
        private int _index;

        public KeyModifiers ReadPressed() => script[Math.Min(_index++, script.Length - 1)];
    }

    private readonly FakeClock _clock = new();
    private readonly FakeInjector _injector = new();
    private IntPtr _foreground = new(1234);

    private TypeTextActionExecutor Create(IModifierStateReader? modifiers = null) =>
        new(
            _injector,
            modifiers ?? new ScriptedModifiers(KeyModifiers.None),
            _clock,
            () => _foreground,
            pause: (interval, token) =>
            {
                _clock.Now += interval;
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

    private static string Describe(IEnumerable<KeyboardEvent> events) =>
        string.Join(" ", events.Select(e => $"{(e.KeyUp ? '-' : '+')}{(e.Unicode ? "U" : "K")}{e.Code:X2}"));

    // La secuencia

    [Fact]
    public void EachCharacter_IsAUnicodePressAndRelease_InOrder()
    {
        Assert.Equal("+U48 -U48 +U69 -U69", Describe(TextSequence.Build("Hi")));
    }

    [Fact]
    public void ALineBreak_IsOneEnterKey_WhateverItsForm()
    {
        Assert.Equal("+U61 -U61 +K0D -K0D +U62 -U62", Describe(TextSequence.Build("a\r\nb")));
        Assert.Equal("+U61 -U61 +K0D -K0D +U62 -U62", Describe(TextSequence.Build("a\nb")));
        Assert.Equal("+U61 -U61 +K0D -K0D +U62 -U62", Describe(TextSequence.Build("a\rb")));
        Assert.Equal("+K0D -K0D +K0D -K0D", Describe(TextSequence.Build("\n\n")));
    }

    [Fact]
    public void ATab_IsTheTabKey()
    {
        Assert.Equal("+U61 -U61 +K09 -K09", Describe(TextSequence.Build("a\t")));
    }

    [Fact]
    public void ASurrogatePair_IsSentAsItsTwoUtf16Units_AsWindowsDocuments()
    {
        // 😀 = U+1F600 = D83D DE00
        Assert.Equal("+UD83D -UD83D +UDE00 -UDE00", Describe(TextSequence.Build("😀")));
    }

    [Fact]
    public void TheSequence_NeverUsesModifierKeys()
    {
        var events = TextSequence.Build("Hola Ñandú, ¿qué tal? @#€\r\n");

        Assert.DoesNotContain(events, e => !e.Unicode && e.Code is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C);
    }

    // El ejecutor

    [Fact]
    public async Task TheText_IsSentInASingleCall_AndTheClipboardIsNotInvolved()
    {
        var result = await Create().ExecuteAsync(new TypeTextAction("Ok"));

        Assert.True(result.IsSuccess);
        Assert.Equal("+U4F -U4F +U6B -U6B", Describe(Assert.Single(_injector.Sent)));
    }

    [Fact]
    public async Task ItWaitsForTheGestureCtrlToBeReleased_BeforeTyping()
    {
        var result = await Create(new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.Control, KeyModifiers.None)).ExecuteAsync(new TypeTextAction("x"));

        Assert.True(result.IsSuccess);
        Assert.Single(_injector.Sent);
        Assert.True(_clock.Now > TimeSpan.Zero); // hubo esperas
    }

    [Fact]
    public async Task IfTheModifiersAreNeverReleased_ItTimesOut_SendsNothing_AndDoesNotRepeatTheText()
    {
        var result = await Create(new ScriptedModifiers(KeyModifiers.Control | KeyModifiers.Alt)).ExecuteAsync(new TypeTextAction("contraseña-secreta"));

        Assert.Equal(ActionErrorCodes.ModifiersNotReleased, result.Error!.Code);
        Assert.Contains("Ctrl", result.Error.Message);
        Assert.Contains("Alt", result.Error.Message);
        Assert.DoesNotContain("secreta", result.Error.Message);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task WithoutAForegroundWindow_NothingIsSent()
    {
        _foreground = IntPtr.Zero;

        var result = await Create().ExecuteAsync(new TypeTextAction("x"));

        Assert.Equal(ActionErrorCodes.ForegroundChanged, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task IfTheForegroundWindowChangesWhileWaiting_NothingIsSent()
    {
        var changed = false;
        var executor = new TypeTextActionExecutor(
            _injector,
            new ScriptedModifiers(KeyModifiers.Control, KeyModifiers.None),
            _clock,
            () => _foreground,
            pause: (interval, _) =>
            {
                _clock.Now += interval;
                if (!changed)
                {
                    changed = true;
                    _foreground = new IntPtr(999);
                }

                return Task.CompletedTask;
            });

        var result = await executor.ExecuteAsync(new TypeTextAction("x"));

        Assert.Equal(ActionErrorCodes.ForegroundChanged, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task ACancelledToken_SendsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await Create().ExecuteAsync(new TypeTextAction("x"), cancellation.Token);

        Assert.Equal(ActionExecutionStatus.Cancelled, result.Status);
        Assert.Empty(_injector.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    [InlineData("campana\u0007")]
    public async Task AnInvalidText_IsRejected_AndNothingIsSent(string text)
    {
        var result = await Create().ExecuteAsync(new TypeTextAction(text));

        Assert.Equal(ActionErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public async Task AnotherKindOfAction_IsNotSupported()
    {
        var executor = Create();
        var action = new WindowControlAction(WindowOperation.Close);

        Assert.False(executor.CanExecute(action));
        Assert.Equal(ActionErrorCodes.UnsupportedAction, (await executor.ExecuteAsync(action)).Error!.Code);
        Assert.True(executor.CanExecute(new TypeTextAction("x")));
    }

    [Fact]
    public async Task IfWindowsRefusesTheInput_ItIsReported_WithoutTheText()
    {
        _injector.Result = new KeyInjectionResult(false, 2, 5);

        var result = await Create().ExecuteAsync(new TypeTextAction("dato-privado"));

        Assert.Equal(ActionErrorCodes.InputInjectionFailed, result.Error!.Code);
        Assert.Contains("5", result.Error.Message);
        Assert.DoesNotContain("privado", result.Error.Message);
    }

    [Fact]
    public async Task IfTheReleaseCannotBeConfirmed_ItIsAKeyReleaseFailure()
    {
        _injector.Result = new KeyInjectionResult(false, 2, 5, ReleaseFailed: true);

        var result = await Create().ExecuteAsync(new TypeTextAction("x"));

        Assert.Equal(ActionErrorCodes.KeyReleaseFailed, result.Error!.Code);
    }

    [Theory]
    [InlineData(true, ActionErrorCodes.InputInjectionFailed)]
    [InlineData(false, ActionErrorCodes.KeyReleaseFailed)]
    public async Task IfTheSystemCallThrows_TheFailureIsReported_NeverAsSuccess(bool releaseConfirmed, string code)
    {
        _injector.Throw = new KeyInjectionException("x", new InvalidOperationException("detalle-interno"), releaseConfirmed);

        var result = await Create().ExecuteAsync(new TypeTextAction("x"));

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Contains("InvalidOperationException", result.Error.Message);
        Assert.DoesNotContain("detalle-interno", result.Error.Message);
    }

    [Fact]
    public void TheOptions_MustBePositive()
    {
        var bad = new ShortcutExecutorOptions { ModifierReleaseTimeout = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TypeTextActionExecutor(_injector, new ScriptedModifiers(KeyModifiers.None), _clock, () => _foreground, bad));
    }

    // El inyector real, con la llamada al sistema sustituida

    [Fact]
    public void TheRealInjector_SendsUnicodeEventsWithoutAVirtualKey_AndWithTheUnicodeFlag()
    {
        var calls = new List<NativeMethods.INPUT[]>();
        var injector = new SendInputKeyboardInjector(inputs =>
        {
            calls.Add(inputs);
            return ((uint)inputs.Length, 0);
        });

        var result = injector.Send(TextSequence.Build("Añ"));

        Assert.True(result.Success);
        var inputs = Assert.Single(calls);
        Assert.Equal(4, inputs.Length);
        Assert.All(inputs, input => Assert.Equal(0, input.ki.wVk));
        Assert.Equal((ushort)'A', inputs[0].ki.wScan);
        Assert.Equal((ushort)'ñ', inputs[2].ki.wScan);
        Assert.Equal(0x0004u, inputs[0].ki.dwFlags);              // KEYEVENTF_UNICODE, pulsar
        Assert.Equal(0x0004u | 0x0002u, inputs[1].ki.dwFlags);    // KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, soltar
        Assert.All(inputs, input => Assert.Equal(IntPtr.Zero, input.ki.dwExtraInfo)); // sin la marca de la autoprueba
    }

    [Fact]
    public void ARegularKey_StillCarriesItsVirtualKey_AndNoUnicodeFlag()
    {
        var calls = new List<NativeMethods.INPUT[]>();
        var injector = new SendInputKeyboardInjector(inputs =>
        {
            calls.Add(inputs);
            return ((uint)inputs.Length, 0);
        });

        injector.Send([new KeyboardEvent(0x0D, false, false), new KeyboardEvent(0x0D, false, true)]);

        var inputs = Assert.Single(calls);
        Assert.Equal(0x0D, inputs[0].ki.wVk);
        Assert.Equal(0, inputs[0].ki.wScan);
        Assert.Equal(0u, inputs[0].ki.dwFlags);
        Assert.Equal(0x0002u, inputs[1].ki.dwFlags);
    }

    [Fact]
    public void IfOnlyPartOfTheTextIsAccepted_TheUnicodeKeysLeftPressedAreReleased_AsUnicode()
    {
        var calls = new List<NativeMethods.INPUT[]>();
        var injector = new SendInputKeyboardInjector(inputs =>
        {
            calls.Add(inputs);
            return calls.Count == 1 ? (1u, 5) : ((uint)inputs.Length, 0); // solo entró «+a»
        });

        var result = injector.Send(TextSequence.Build("ab"));

        Assert.False(result.Success);
        Assert.False(result.ReleaseFailed);
        var release = Assert.Single(calls.Skip(1).Single());
        Assert.Equal((ushort)'a', release.ki.wScan);
        Assert.Equal(0x0004u | 0x0002u, release.ki.dwFlags);
    }

    [Fact]
    public void ReleasesFor_DistinguishesAUnicodeCharacterFromAVirtualKeyWithTheSameCode()
    {
        // +VK_RETURN (0x0D) y +U+000D sin soltar: las dos hay que soltarlas, cada una a su manera.
        IReadOnlyList<KeyboardEvent> sent = [new KeyboardEvent(0x0D, false, false), new KeyboardEvent(0x0D, false, false, Unicode: true), new KeyboardEvent(0x0D, false, true)];

        var releases = ShortcutSequence.ReleasesFor(sent);

        var remaining = Assert.Single(releases);
        Assert.True(remaining.Unicode);
        Assert.True(remaining.KeyUp);
    }
}
