using Radial.Windows.Input;
using Radial.Windows.Native;
using Radial.Windows.Shortcuts;

namespace Radial.Windows.Tests.Shortcuts;

/// <summary>El inyector real, con la llamada al sistema sustituida: nunca se envía entrada de verdad.</summary>
public class SendInputKeyboardInjectorTests
{
    private sealed class FakeSystem
    {
        public List<NativeMethods.INPUT[]> Calls { get; } = [];

        /// <summary>Cuántos eventos acepta en la primera llamada (null = todos).</summary>
        public uint? AcceptOnFirstCall { get; set; }

        public int Error { get; set; } = 5;

        public (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
        {
            Calls.Add(inputs);
            if (Calls.Count == 1 && AcceptOnFirstCall is { } accepted && accepted < inputs.Length)
            {
                return (accepted, Error);
            }

            return ((uint)inputs.Length, 0);
        }
    }

    private static readonly KeyboardEvent[] CtrlC =
    [
        new(0xA2, false, false),
        new(0x43, false, false),
        new(0x43, false, true),
        new(0xA2, false, true),
    ];

    [Fact]
    public void ASuccessfulSend_IsASingleSystemCall_WithAllTheEvents()
    {
        var system = new FakeSystem();

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.True(result.Success);
        Assert.Equal(4, result.EventsInserted);
        var call = Assert.Single(system.Calls); // atómico: nada se intercala dentro de la secuencia
        Assert.Equal(4, call.Length);
    }

    [Fact]
    public void TheEventsAreKeyboardInput_WithTheRightCodesAndFlags()
    {
        var system = new FakeSystem();
        KeyboardEvent[] events = [new(0x5B, true, false), new(0x45, false, false), new(0x45, false, true), new(0x5B, true, true)];

        new SendInputKeyboardInjector(system.Send).Send(events);

        var inputs = system.Calls.Single();
        Assert.All(inputs, i => Assert.Equal(1u, i.type)); // INPUT_KEYBOARD
        Assert.Equal([0x5B, 0x45, 0x45, 0x5B], inputs.Select(i => (int)i.ki.wVk));
        Assert.Equal([0x1u, 0x0u, 0x2u, 0x3u], inputs.Select(i => i.ki.dwFlags)); // EXTENDED, nada, KEYUP, EXTENDED|KEYUP
    }

    [Fact]
    public void TheEvents_DoNotCarryTheSelfTestMarker_SoTheCaptureHooksAlwaysIgnoreThem()
    {
        var system = new FakeSystem();

        new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.All(system.Calls.Single(), i =>
        {
            Assert.Equal(IntPtr.Zero, i.ki.dwExtraInfo);
            Assert.NotEqual((IntPtr)SyntheticMarker.Value, i.ki.dwExtraInfo);
        });
    }

    [Fact]
    public void WhenTheSystemAcceptsOnlyPartOfTheSequence_WhatWasLeftPressedIsReleased()
    {
        var system = new FakeSystem { AcceptOnFirstCall = 2, Error = 5 }; // entraron +Ctrl y +C

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.False(result.Success);
        Assert.Equal(2, result.EventsInserted);
        Assert.Equal(5, result.Win32Error);
        Assert.Equal(2, system.Calls.Count);
        var release = system.Calls[1];
        Assert.Equal([0x43, 0xA2], release.Select(i => (int)i.ki.wVk)); // C y después Ctrl
        Assert.All(release, i => Assert.Equal(2u, i.ki.dwFlags)); // KEYUP
    }

    [Fact]
    public void WhenOnlyTheReleasingTailIsMissing_OnlyTheModifierIsReleased()
    {
        var system = new FakeSystem { AcceptOnFirstCall = 3 }; // +Ctrl +C -C

        new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.Equal([0xA2], system.Calls[1].Select(i => (int)i.ki.wVk));
    }

    [Fact]
    public void WhenTheSystemAcceptsNothing_ThereIsNothingToRelease_AndTheFailureIsReported()
    {
        var system = new FakeSystem { AcceptOnFirstCall = 0, Error = 5 };

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.False(result.Success);
        Assert.Equal(0, result.EventsInserted);
        Assert.Equal(5, result.Win32Error);
        Assert.Single(system.Calls);
    }

    // Liberación comprobada (nada de teclas atascadas sin saberlo)

    /// <summary>Sistema falso guiado por un guion: cada llamada recibe lo que se envió y decide cuántos eventos «acepta».</summary>
    private sealed class ScriptedSystem(params Func<NativeMethods.INPUT[], (uint Inserted, int Win32Error)>[] script)
    {
        public List<NativeMethods.INPUT[]> Calls { get; } = [];

        public (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
        {
            var step = script[Math.Min(Calls.Count, script.Length - 1)];
            Calls.Add(inputs);
            return step(inputs);
        }
    }

    private static Func<NativeMethods.INPUT[], (uint, int)> Accept(uint count, int error = 5) => inputs => (Math.Min(count, (uint)inputs.Length), error);

    private static Func<NativeMethods.INPUT[], (uint, int)> AcceptAll() => inputs => ((uint)inputs.Length, 0);

    private static Func<NativeMethods.INPUT[], (uint, int)> Throw() => _ => throw new InvalidOperationException("boom");

    private static int[] Codes(NativeMethods.INPUT[] inputs) => inputs.Select(i => (int)i.ki.wVk).ToArray();

    [Fact]
    public void AConfirmedRelease_IsNotReportedAsFailed()
    {
        var system = new ScriptedSystem(Accept(2), AcceptAll());

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.False(result.Success);
        Assert.False(result.ReleaseFailed);
        Assert.Equal(2, system.Calls.Count);
    }

    [Fact]
    public void IfTheReleaseIsPartial_TheMissingPartIsRetried_AndOnlyThatPart()
    {
        // Entran +Ctrl +C; la liberación de [-C, -Ctrl] entra a medias (solo -C); se reintenta solo -Ctrl.
        var system = new ScriptedSystem(Accept(2), Accept(1), AcceptAll());

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.False(result.ReleaseFailed);
        Assert.Equal(3, system.Calls.Count);
        Assert.Equal([0x43, 0xA2], Codes(system.Calls[1]));
        Assert.Equal([0xA2], Codes(system.Calls[2]));
        Assert.All(system.Calls[2], i => Assert.Equal(2u, i.ki.dwFlags)); // KEYUP
    }

    [Fact]
    public void IfTheReleaseKeepsFailing_ItIsRetriedOnlyOnce_AndTheFailureIsReported()
    {
        // Nunca un bucle: la sesión original + la liberación + UN reintento, y se avisa de que puede haber teclas pulsadas.
        var system = new ScriptedSystem(Accept(2), Accept(0));

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.False(result.Success);
        Assert.True(result.ReleaseFailed);
        Assert.Equal(2, result.EventsInserted);
        Assert.Equal(3, system.Calls.Count);
        Assert.Equal(Codes(system.Calls[1]), Codes(system.Calls[2]));
    }

    [Fact]
    public void IfTheReleaseThrows_ItIsRetried_AndTheOutcomeDependsOnTheRetry()
    {
        var recovers = new ScriptedSystem(Accept(2), Throw(), AcceptAll());
        var fails = new ScriptedSystem(Accept(2), Throw(), Throw());

        var first = new SendInputKeyboardInjector(recovers.Send).Send(CtrlC);
        var second = new SendInputKeyboardInjector(fails.Send).Send(CtrlC);

        Assert.False(first.ReleaseFailed);
        Assert.True(second.ReleaseFailed);
        Assert.Equal(3, fails.Calls.Count);
    }

    [Fact]
    public void WhenNothingWasInserted_NoReleaseIsSent_AndNoFailureIsReported()
    {
        var system = new ScriptedSystem(Accept(0));

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.False(result.ReleaseFailed);
        Assert.Single(system.Calls);
    }

    [Fact]
    public void WhenOnlyAModifierWentIn_ItIsReleased()
    {
        var system = new ScriptedSystem(Accept(1), AcceptAll());

        new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.Equal([0xA2], Codes(system.Calls[1]));
    }

    [Fact]
    public void WithSeveralModifiers_TheReleaseIsInReverseOrder()
    {
        KeyboardEvent[] ctrlShiftS =
        [
            new(0xA2, false, false), new(0xA0, false, false), new(0x53, false, false), new(0x53, false, true), new(0xA0, false, true), new(0xA2, false, true),
        ];
        var system = new ScriptedSystem(Accept(3), AcceptAll()); // entraron +Ctrl +Mayús +S

        new SendInputKeyboardInjector(system.Send).Send(ctrlShiftS);

        Assert.Equal([0x53, 0xA0, 0xA2], Codes(system.Calls[1]));
    }

    [Fact]
    public void IfTheSystemCallThrows_EveryKeyTheSequencePressesIsReleased_AndTheFailureIsNeverASuccess()
    {
        // No se sabe cuántos eventos entraron (la API solo lo dice al devolver): se suelta todo lo que la secuencia pulsa.
        var system = new ScriptedSystem(Throw(), AcceptAll());
        var injector = new SendInputKeyboardInjector(system.Send);

        var exception = Assert.Throws<KeyInjectionException>(() => injector.Send(CtrlC));

        Assert.True(exception.ReleaseConfirmed);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(2, system.Calls.Count);
        Assert.Equal([0x43, 0xA2], Codes(system.Calls[1]));
        Assert.All(system.Calls[1], i => Assert.Equal(2u, i.ki.dwFlags));
    }

    [Fact]
    public void IfTheSystemCallThrows_AndTheCleanupFailsToo_ItIsReportedAsUnconfirmed_AfterOneRetry()
    {
        var system = new ScriptedSystem(Throw());
        var injector = new SendInputKeyboardInjector(system.Send);

        var exception = Assert.Throws<KeyInjectionException>(() => injector.Send(CtrlC));

        Assert.False(exception.ReleaseConfirmed);
        Assert.Equal("boom", exception.InnerException!.Message);
        Assert.Equal(3, system.Calls.Count); // intento original + liberación + un reintento
    }

    [Fact]
    public void IfTheSystemCallThrows_AndTheCleanupIsOnlyPartial_TheRetrySendsOnlyWhatIsMissing()
    {
        var system = new ScriptedSystem(Throw(), Accept(1), AcceptAll()); // la liberación [-C, -Ctrl] solo deja pasar -C

        var exception = Assert.Throws<KeyInjectionException>(() => new SendInputKeyboardInjector(system.Send).Send(CtrlC));

        Assert.True(exception.ReleaseConfirmed);
        Assert.Equal([0xA2], Codes(system.Calls[2]));
    }

    [Fact]
    public void AMultiModifierSequenceThatThrows_ReleasesEveryModifier_InReverseOrder()
    {
        KeyboardEvent[] winShiftE =
        [
            new(0x5B, true, false), new(0xA0, false, false), new(0x45, false, false), new(0x45, false, true), new(0xA0, false, true), new(0x5B, true, true),
        ];
        var system = new ScriptedSystem(Throw(), AcceptAll());

        Assert.Throws<KeyInjectionException>(() => new SendInputKeyboardInjector(system.Send).Send(winShiftE));

        Assert.Equal([0x45, 0xA0, 0x5B], Codes(system.Calls[1]));
        Assert.True(system.Calls[1][2].ki.dwFlags == 0x3u); // la tecla Win conserva su indicador de tecla extendida al soltarla
    }

    [Fact]
    public void TheInjector_DoesNotClaimToKnowHowManyEventsWentIn_WhenTheCallThrows()
    {
        // Contrato: con excepción no hay «EventsInserted»; el único dato fiable es si la liberación se confirmó.
        var system = new ScriptedSystem(Throw(), AcceptAll());

        var exception = Assert.Throws<KeyInjectionException>(() => new SendInputKeyboardInjector(system.Send).Send(CtrlC));

        Assert.DoesNotContain("insert", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InvalidOperationException", exception.Message);
        Assert.DoesNotContain("boom", exception.Message); // el texto original no se copia al mensaje
    }

    [Fact]
    public void ASuccessfulSend_NeverSendsReleasesOfItsOwn()
    {
        var system = new ScriptedSystem(AcceptAll());

        var result = new SendInputKeyboardInjector(system.Send).Send(CtrlC);

        Assert.True(result.Success);
        Assert.False(result.ReleaseFailed);
        Assert.Single(system.Calls);
    }

    [Fact]
    public void ASystemFailure_IsReturned_NotThrown()
    {
        var injector = new SendInputKeyboardInjector(_ => (0u, 87));

        var result = injector.Send(CtrlC);

        Assert.False(result.Success);
        Assert.Equal(87, result.Win32Error);
    }

    [Fact]
    public void NothingToSend_IsAProgrammingError()
    {
        var injector = new SendInputKeyboardInjector(new FakeSystem().Send);

        Assert.Throws<ArgumentException>(() => injector.Send([]));
        Assert.Throws<ArgumentNullException>(() => injector.Send(null!));
    }

    [Fact]
    public void TheModifierStateReader_CanBeCalled_WithoutAnyEffect()
    {
        // Lee el estado real del teclado: el valor depende de quién esté pulsando algo ahora; solo se comprueba que no falla.
        var reader = new Win32ModifierStateReader();

        _ = reader.ReadPressed();
    }
}
