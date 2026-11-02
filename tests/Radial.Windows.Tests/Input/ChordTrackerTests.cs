using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>
/// Secuencias simuladas de eventos. Comprueban la lógica del gesto; NO prueban los hooks reales
/// (eso requiere entrada física: ver el procedimiento manual del documento técnico).
/// </summary>
public class ChordTrackerTests
{
    private readonly ChordTracker _tracker = new();

    private ChordResult Send(RawInputEvent e) => _tracker.Process(in e);

    private ChordResult CtrlDown(bool right = false) => Send(RawInputEvent.KeyDown(right ? InputKey.RightControl : InputKey.LeftControl));

    private ChordResult CtrlUp(bool right = false) => Send(RawInputEvent.KeyUp(right ? InputKey.RightControl : InputKey.LeftControl));

    private ChordResult MiddleDown(int x = 10, int y = 20) => Send(RawInputEvent.MiddleDown(x, y));

    private ChordResult MiddleUp(int x = 10, int y = 20) => Send(RawInputEvent.MiddleUp(x, y));

    private ChordResult Move(int x, int y) => Send(RawInputEvent.Move(x, y));

    private ChordResult EscapeDown() => Send(RawInputEvent.KeyDown(InputKey.Escape));

    private ChordResult EscapeUp() => Send(RawInputEvent.KeyUp(InputKey.Escape));

    private void ActivateWithLeftCtrl()
    {
        CtrlDown();
        var activation = MiddleDown();
        Assert.Equal(GestureSignal.Activated, activation.Signal);
    }

    // 1 y 2. Ctrl primero (izquierdo o derecho)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlFirst_ThenMiddle_ActivatesAndConsumesTheClick(bool rightCtrl)
    {
        var ctrl = CtrlDown(rightCtrl);
        var middle = MiddleDown(100, 200);

        Assert.False(ctrl.Consume);
        Assert.Equal(GestureSignal.None, ctrl.Signal);
        Assert.True(middle.Consume);
        Assert.Equal(GestureSignal.Activated, middle.Signal);
        Assert.Equal(new ScreenPoint(100, 200), middle.Point);
        Assert.Equal(ChordPhase.Active, _tracker.Phase);
    }

    [Fact]
    public void BothControls_ReleasingOneKeepsTheGestureAlive()
    {
        CtrlDown();
        CtrlDown(right: true);
        MiddleDown();

        var releaseLeft = CtrlUp();
        Assert.Equal(GestureSignal.None, releaseLeft.Signal);
        Assert.Equal(ChordPhase.Active, _tracker.Phase);

        var releaseRight = CtrlUp(right: true);
        Assert.Equal(GestureSignal.Cancelled, releaseRight.Signal);
        Assert.Equal(CancelCause.CtrlReleased, releaseRight.Cause);
    }

    // 3, 8, 9. Botón central sin Ctrl, y Ctrl pulsado después de un clic foráneo

    [Fact]
    public void MiddleWithoutCtrl_PassesThroughAndDoesNotActivate()
    {
        var down = MiddleDown();
        var up = MiddleUp();

        Assert.False(down.Consume);
        Assert.Equal(GestureSignal.None, down.Signal);
        Assert.False(up.Consume);
        Assert.Equal(GestureSignal.None, up.Signal);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
    }

    [Fact]
    public void MiddleFirst_ThenCtrl_NeverActivates_AndNothingIsInterceptedRetroactively()
    {
        var down = MiddleDown();
        Assert.True(_tracker.Snapshot.ForeignMiddle);

        var ctrl = CtrlDown();
        var move = Move(5, 5);
        var up = MiddleUp();
        CtrlUp();

        Assert.False(down.Consume);
        Assert.False(ctrl.Consume);
        Assert.Equal(GestureSignal.None, move.Signal);
        Assert.False(up.Consume);
        Assert.Equal(GestureSignal.None, up.Signal);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
        Assert.False(_tracker.Snapshot.ForeignMiddle);
    }

    [Fact]
    public void ForeignClick_DoesNotPreventTheNextCtrlFirstGesture()
    {
        MiddleDown();
        CtrlDown();
        MiddleUp(); // termina el clic foráneo, con Ctrl todavía pulsado

        var down = MiddleDown();

        Assert.Equal(GestureSignal.Activated, down.Signal);
        Assert.True(down.Consume);
    }

    // 5. Soltar el botón central confirma

    [Fact]
    public void ReleasingMiddle_Confirms_ConsumesTheRelease_AndReportsTheReleasePosition()
    {
        ActivateWithLeftCtrl();
        Move(30, 40);

        var up = MiddleUp(55, 66);

        Assert.True(up.Consume);
        Assert.Equal(GestureSignal.Confirmed, up.Signal);
        Assert.Equal(new ScreenPoint(55, 66), up.Point);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
    }

    [Fact]
    public void ReleasingCtrlAfterConfirming_DoesNothing()
    {
        ActivateWithLeftCtrl();
        MiddleUp();

        var ctrlUp = CtrlUp();

        Assert.Equal(GestureSignal.None, ctrlUp.Signal);
        Assert.False(ctrlUp.Consume);
    }

    [Fact]
    public void Moves_ArePublishedWhileActive_AndNeverConsumed()
    {
        ActivateWithLeftCtrl();

        var move = Move(7, 9);

        Assert.Equal(GestureSignal.Moved, move.Signal);
        Assert.Equal(new ScreenPoint(7, 9), move.Point);
        Assert.False(move.Consume);
    }

    [Fact]
    public void Moves_AreNotPublishedWhenIdle_AndNeverConsumed()
    {
        var move = Move(7, 9);

        Assert.Equal(GestureSignal.None, move.Signal);
        Assert.False(move.Consume);
    }

    // 4 y 11. Soltar Ctrl antes de confirmar; el estado consumido sobrevive a la cancelación

    [Fact]
    public void ReleasingCtrlBeforeConfirming_Cancels_NeverConsumesCtrl_AndStillConsumesTheMiddleRelease()
    {
        ActivateWithLeftCtrl();

        var ctrlUp = CtrlUp();
        var phaseAfterCancel = _tracker.Phase;
        var up = MiddleUp();

        Assert.False(ctrlUp.Consume);
        Assert.Equal(GestureSignal.Cancelled, ctrlUp.Signal);
        Assert.Equal(CancelCause.CtrlReleased, ctrlUp.Cause);
        Assert.Equal(ChordPhase.AwaitingMiddleRelease, phaseAfterCancel);
        Assert.True(up.Consume);
        Assert.Equal(GestureSignal.None, up.Signal); // no se confirma
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
    }

    [Fact]
    public void AfterCancelling_MovesAreNoLongerPublished()
    {
        ActivateWithLeftCtrl();
        CtrlUp();

        var move = Move(1, 1);

        Assert.Equal(GestureSignal.None, move.Signal);
    }

    [Fact]
    public void AfterCancelling_PressingCtrlAgainDoesNotReviveTheGesture()
    {
        ActivateWithLeftCtrl();
        CtrlUp();
        CtrlDown();

        var up = MiddleUp();

        Assert.Equal(GestureSignal.None, up.Signal);
        Assert.True(up.Consume);
    }

    // 6. Escape

    [Fact]
    public void Escape_CancelsTheActiveGesture_AndIsConsumedWithItsRelease()
    {
        ActivateWithLeftCtrl();

        var down = EscapeDown();
        var up = EscapeUp();
        var middleUp = MiddleUp();

        Assert.True(down.Consume);
        Assert.Equal(GestureSignal.Cancelled, down.Signal);
        Assert.Equal(CancelCause.Escape, down.Cause);
        Assert.True(up.Consume);
        Assert.True(middleUp.Consume);
        Assert.Equal(GestureSignal.None, middleUp.Signal);
    }

    [Fact]
    public void Escape_AutoRepeatsAfterCancelling_AreConsumedUntilReleased()
    {
        ActivateWithLeftCtrl();
        EscapeDown();

        var repeat = EscapeDown();
        var up = EscapeUp();
        var next = EscapeDown();

        Assert.True(repeat.Consume);
        Assert.Equal(GestureSignal.None, repeat.Signal);
        Assert.True(up.Consume);
        Assert.False(next.Consume);
    }

    [Fact]
    public void Escape_WhenIdle_PassesThrough()
    {
        var down = EscapeDown();
        var up = EscapeUp();

        Assert.False(down.Consume);
        Assert.False(up.Consume);
    }

    [Fact]
    public void Escape_ReleasedAfterTheGestureEnded_IsNotLeftConsumingForever()
    {
        ActivateWithLeftCtrl();
        EscapeDown();
        MiddleUp(); // termina el gesto con Escape aún pulsado

        var laterEscape = EscapeDown();

        Assert.False(laterEscape.Consume);
    }

    // 7. Rueda

    [Fact]
    public void Wheel_NeverActivates_NorIsConsumed()
    {
        CtrlDown();

        var wheel = Send(RawInputEvent.Wheel(3, 4));

        Assert.False(wheel.Consume);
        Assert.Equal(GestureSignal.None, wheel.Signal);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
    }

    [Fact]
    public void Wheel_DuringAnActiveGesture_IsIgnored()
    {
        ActivateWithLeftCtrl();

        var wheel = Send(RawInputEvent.Wheel());

        Assert.False(wheel.Consume);
        Assert.Equal(GestureSignal.None, wheel.Signal);
        Assert.Equal(ChordPhase.Active, _tracker.Phase);
    }

    // 10, 14. Dobles activaciones y secuencias incorrectas

    [Fact]
    public void RepeatedMiddleDown_WhileActive_IsConsumedWithoutActivatingAgain()
    {
        ActivateWithLeftCtrl();
        var id = _tracker.ActivationId;

        var repeated = MiddleDown();

        Assert.True(repeated.Consume);
        Assert.Equal(GestureSignal.None, repeated.Signal);
        Assert.Equal(id, _tracker.ActivationId);
    }

    [Fact]
    public void RepeatedMiddleDown_OnAForeignClick_PassesThrough()
    {
        MiddleDown();

        var repeated = MiddleDown();

        Assert.False(repeated.Consume);
    }

    [Fact]
    public void RepeatedCtrlDown_AutoRepeat_IsHarmless()
    {
        CtrlDown();
        CtrlDown();
        CtrlDown();

        var middle = MiddleDown();

        Assert.Equal(GestureSignal.Activated, middle.Signal);
        Assert.Equal(1, _tracker.ActivationId);
    }

    [Fact]
    public void MiddleUpWithoutADown_PassesThrough()
    {
        var up = MiddleUp();

        Assert.False(up.Consume);
        Assert.Equal(GestureSignal.None, up.Signal);
    }

    [Fact]
    public void CtrlUpWithoutADown_IsHarmless_AndDoesNotCancelAnything()
    {
        var up = CtrlUp();

        Assert.False(up.Consume);
        Assert.Equal(GestureSignal.None, up.Signal);
    }

    [Fact]
    public void MiddleDownWhileWaitingForTheCancelledRelease_IsConsumedAndDoesNotActivate()
    {
        ActivateWithLeftCtrl();
        CtrlUp();
        CtrlDown();

        var down = MiddleDown();

        Assert.True(down.Consume);
        Assert.Equal(GestureSignal.None, down.Signal);
        Assert.Equal(1, _tracker.ActivationId);
    }

    [Fact]
    public void ConsecutiveGestures_EachGetTheirOwnActivationId()
    {
        ActivateWithLeftCtrl();
        MiddleUp();
        var second = MiddleDown();

        Assert.Equal(GestureSignal.Activated, second.Signal);
        Assert.Equal(2, _tracker.ActivationId);
    }

    // 12. Eventos inyectados

    [Fact]
    public void InjectedEvents_AreIgnoredByDefault()
    {
        var ctrl = Send(RawInputEvent.KeyDown(InputKey.LeftControl, injected: true));
        var middle = Send(RawInputEvent.MiddleDown(1, 1, injected: true));

        Assert.False(ctrl.Consume);
        Assert.Equal(GestureSignal.None, middle.Signal);
        Assert.False(middle.Consume);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
        Assert.False(_tracker.IsCtrlDown);
    }

    [Fact]
    public void InjectedMiddle_WithARealCtrl_DoesNotActivate()
    {
        CtrlDown();

        var middle = Send(RawInputEvent.MiddleDown(1, 1, injected: true));

        Assert.Equal(GestureSignal.None, middle.Signal);
    }

    [Fact]
    public void InjectedEvents_DoNotDisturbAnActiveGesture()
    {
        ActivateWithLeftCtrl();

        var ctrlUp = Send(RawInputEvent.KeyUp(InputKey.LeftControl, injected: true));
        var escape = Send(RawInputEvent.KeyDown(InputKey.Escape, injected: true));
        var middleUp = Send(RawInputEvent.MiddleUp(0, 0, injected: true));

        Assert.Equal(GestureSignal.None, ctrlUp.Signal);
        Assert.False(escape.Consume);
        Assert.Equal(GestureSignal.None, middleUp.Signal);
        Assert.Equal(ChordPhase.Active, _tracker.Phase);
    }

    [Fact]
    public void OwnSyntheticEvents_AreIgnored_WhenTheDiagnosticModeIsOff()
    {
        Assert.False(_tracker.AcceptOwnSyntheticInput);

        Send(RawInputEvent.KeyDown(InputKey.LeftControl, ownSynthetic: true));
        var middle = Send(RawInputEvent.MiddleDown(1, 1, ownSynthetic: true));

        Assert.Equal(GestureSignal.None, middle.Signal);
        Assert.False(middle.Consume);
        Assert.False(_tracker.IsCtrlDown);
    }

    [Fact]
    public void OwnSyntheticEvents_AreAccepted_OnlyWhenExplicitlyEnabled()
    {
        _tracker.AcceptOwnSyntheticInput = true;

        Send(RawInputEvent.KeyDown(InputKey.LeftControl, ownSynthetic: true));
        var middle = Send(RawInputEvent.MiddleDown(1, 1, ownSynthetic: true));

        Assert.Equal(GestureSignal.Activated, middle.Signal);
    }

    [Fact]
    public void InjectedEventsFromOtherApps_AreIgnored_EvenInDiagnosticMode()
    {
        _tracker.AcceptOwnSyntheticInput = true;

        var ctrl = Send(RawInputEvent.KeyDown(InputKey.LeftControl, injected: true));
        var middle = Send(RawInputEvent.MiddleDown(1, 1, injected: true));

        Assert.False(ctrl.Consume);
        Assert.Equal(GestureSignal.None, middle.Signal);
        Assert.False(middle.Consume);
        Assert.False(_tracker.IsCtrlDown);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
    }

    [Fact]
    public void InjectedEventsFromOtherApps_DoNotDisturbAnActiveSyntheticGesture_InDiagnosticMode()
    {
        _tracker.AcceptOwnSyntheticInput = true;
        Send(RawInputEvent.KeyDown(InputKey.LeftControl, ownSynthetic: true));
        Send(RawInputEvent.MiddleDown(1, 1, ownSynthetic: true));

        var foreignCtrlUp = Send(RawInputEvent.KeyUp(InputKey.LeftControl, injected: true));
        var foreignEscape = Send(RawInputEvent.KeyDown(InputKey.Escape, injected: true));
        var foreignMiddleUp = Send(RawInputEvent.MiddleUp(0, 0, injected: true));

        Assert.Equal(GestureSignal.None, foreignCtrlUp.Signal);
        Assert.False(foreignEscape.Consume);
        Assert.Equal(GestureSignal.None, foreignMiddleUp.Signal);
        Assert.Equal(ChordPhase.Active, _tracker.Phase);
    }

    [Fact]
    public void PhysicalEvents_AreAlwaysAccepted_InDiagnosticMode()
    {
        _tracker.AcceptOwnSyntheticInput = true;

        Send(RawInputEvent.KeyDown(InputKey.LeftControl));
        var middle = Send(RawInputEvent.MiddleDown(1, 1));

        Assert.Equal(GestureSignal.Activated, middle.Signal);
    }

    // 13. Cancelación del watchdog (la política vive en el servicio; aquí, su efecto sobre el tracker)

    [Fact]
    public void CancelBecauseCtrlLost_Cancels_ForgetsTheStaleCtrl_AndKeepsConsumingTheMiddleRelease()
    {
        ActivateWithLeftCtrl();

        var result = _tracker.CancelBecauseCtrlLost();

        Assert.Equal(GestureSignal.Cancelled, result.Signal);
        Assert.Equal(CancelCause.WatchdogCtrlLost, result.Cause);
        Assert.False(_tracker.IsCtrlDown);
        Assert.Equal(ChordPhase.AwaitingMiddleRelease, _tracker.Phase);
        var up = MiddleUp();
        Assert.True(up.Consume);
        Assert.Equal(GestureSignal.None, up.Signal);
    }

    [Fact]
    public void CancelBecauseCtrlLost_WhenIdle_DoesNothing()
    {
        Assert.Equal(GestureSignal.None, _tracker.CancelBecauseCtrlLost().Signal);
    }

    [Fact]
    public void WatchdogCancellation_NeverConfirms_EvenIfTheReleaseArrivesLater()
    {
        ActivateWithLeftCtrl();
        _tracker.Cancel(CancelCause.WatchdogMouseHookSilent);

        var up = MiddleUp(5, 5);

        Assert.NotEqual(GestureSignal.Confirmed, up.Signal);
        Assert.True(up.Consume);
    }

    // Cancelación externa

    [Fact]
    public void ExternalCancel_WhenActive_CancelsAndWaitsForTheRelease()
    {
        ActivateWithLeftCtrl();
        Move(8, 9);

        var result = _tracker.Cancel(CancelCause.Manual);

        Assert.Equal(GestureSignal.Cancelled, result.Signal);
        Assert.Equal(CancelCause.Manual, result.Cause);
        Assert.Equal(new ScreenPoint(8, 9), result.Point);
        Assert.True(MiddleUp().Consume);
    }

    [Fact]
    public void ExternalCancel_WhenIdle_DoesNothing()
    {
        Assert.Equal(GestureSignal.None, _tracker.Cancel(CancelCause.Manual).Signal);
    }

    // 15. Reinicio

    [Fact]
    public void Reset_ForgetsEverything()
    {
        ActivateWithLeftCtrl();
        EscapeDown();

        _tracker.Reset();

        var snapshot = _tracker.Snapshot;
        Assert.False(snapshot.LeftControl);
        Assert.False(snapshot.Middle);
        Assert.Equal(ChordPhase.Idle, snapshot.Phase);
        Assert.False(EscapeDown().Consume);
        Assert.False(MiddleDown().Consume); // sin Ctrl conocido: foráneo
    }

    [Fact]
    public void Seed_WithCtrlAlreadyDown_AllowsTheGestureWithoutSeeingTheCtrlEvent()
    {
        _tracker.Seed(new PhysicalInputState(LeftControl: true, RightControl: false, Middle: false));

        var middle = MiddleDown();

        Assert.Equal(GestureSignal.Activated, middle.Signal);
    }

    [Fact]
    public void Seed_WithTheMiddleButtonAlreadyDown_TreatsItAsForeign()
    {
        _tracker.Seed(new PhysicalInputState(LeftControl: true, RightControl: false, Middle: true));

        var up = MiddleUp();
        var next = MiddleDown();

        Assert.False(up.Consume);
        Assert.Equal(GestureSignal.Activated, next.Signal);
    }

    [Fact]
    public void Restart_AfterAnActiveGesture_StartsClean()
    {
        ActivateWithLeftCtrl();
        _tracker.Seed(default);

        var down = MiddleDown();

        Assert.Equal(GestureSignal.None, down.Signal);
        Assert.False(down.Consume);
        Assert.Equal(ChordPhase.Idle, _tracker.Phase);
    }
}
