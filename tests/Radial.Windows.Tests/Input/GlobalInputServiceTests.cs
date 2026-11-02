using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>
/// Lógica del servicio (ciclo de vida, orden de eventos, watchdog, salud) con hooks, temporizadores y eventos del sistema FALSOS.
/// No prueba los hooks reales de Windows.
/// </summary>
public class GlobalInputServiceTests : IDisposable
{
    private readonly FakeHookHost _host = new();
    private readonly FakePhysicalReader _physical = new();
    private readonly FakeTimerFactory _timers = new();
    private readonly FakeSystemEvents _system = new();
    private readonly GlobalInputService _service;

    public GlobalInputServiceTests()
    {
        _service = new GlobalInputService(_host, _physical, _timers, _system);
    }

    public void Dispose() => _service.Dispose();

    private List<GestureEvent> Drain()
    {
        var events = new List<GestureEvent>();
        while (_service.Events.TryRead(out var e))
        {
            events.Add(e);
        }

        return events;
    }

    private void StartService()
    {
        var result = _service.Start();
        Assert.True(result.Success, result.Message);
    }

    private bool Send(RawInputEvent e) => _host.Send(e);

    private void Activate(int x = 100, int y = 100)
    {
        Send(RawInputEvent.KeyDown(InputKey.LeftControl));
        Assert.True(Send(RawInputEvent.MiddleDown(x, y)));
    }

    // Ciclo de vida

    [Fact]
    public void NewService_IsStopped_AndInstallsNothing()
    {
        Assert.Equal(CaptureState.Stopped, _service.State);
        Assert.Equal(0, _host.StartCalls);
        Assert.False(_service.GetDiagnostics().KeyboardHook.Installed);
    }

    [Fact]
    public void Start_InstallsBothHooks_AndReportsRunning()
    {
        StartService();

        var diagnostics = _service.GetDiagnostics();
        Assert.Equal(CaptureState.Running, _service.State);
        Assert.True(diagnostics.KeyboardHook.Installed);
        Assert.True(diagnostics.MouseHook.Installed);
        Assert.Equal(1, _host.StartCalls);
        Assert.Equal(1, _system.AttachCalls);
        Assert.True(_timers.Health.Running);
    }

    [Fact]
    public void Start_WhileRunning_ThrowsAndDoesNotInstallTwice()
    {
        StartService();

        Assert.Throws<InvalidOperationException>(() => _service.Start());
        Assert.Equal(1, _host.StartCalls);
    }

    [Fact]
    public void Start_WhenAHookFails_ReportsItAndLeavesNothingInstalled()
    {
        _host.MouseResult = new HookInstallStatus(true, false, 1428);

        var result = _service.Start();

        Assert.False(result.Success);
        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(1, _host.StopCalls);
        var diagnostics = _service.GetDiagnostics();
        Assert.False(diagnostics.KeyboardHook.Installed); // no se muestra como instalado un hook que se descartó
        Assert.False(diagnostics.MouseHook.Installed);
        Assert.Equal(1428, diagnostics.MouseHook.Win32Error);
        Assert.Contains(diagnostics.Errors, e => e.Contains("1428"));
    }

    [Fact]
    public void Start_AfterAFailure_CanBeRetried()
    {
        _host.KeyboardResult = new HookInstallStatus(true, false, 5);
        Assert.False(_service.Start().Success);

        _host.KeyboardResult = new HookInstallStatus(true, true, 0);
        var retry = _service.Start();

        Assert.True(retry.Success);
        Assert.Equal(CaptureState.Running, _service.State);
    }

    [Fact]
    public void Stop_ReleasesTheHooks_AndAllowsRestarting()
    {
        StartService();

        _service.Stop();

        Assert.Equal(CaptureState.Stopped, _service.State);
        Assert.Equal(1, _host.StopCalls);
        Assert.Equal(1, _system.DetachCalls);
        Assert.False(_timers.Health.Running);
        Assert.False(_service.GetDiagnostics().MouseHook.Installed);

        StartService();
        Assert.Equal(CaptureState.Running, _service.State);
        Assert.Equal(2, _host.StartCalls);
    }

    [Fact]
    public void Stop_WhenNotStarted_IsHarmless()
    {
        _service.Stop();

        Assert.Equal(0, _host.StopCalls);
    }

    [Fact]
    public void Dispose_StopsTheServiceAndCompletesTheEventChannel()
    {
        StartService();

        _service.Dispose();

        Assert.Equal(1, _host.StopCalls);
        Assert.True(_service.Events.Completion.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => _service.Start());
    }

    // Eventos críticos, orden y consumo

    [Fact]
    public void CtrlFirstGesture_PublishesActivatedThenConfirmed_InOrder_WithPositions()
    {
        StartService();

        Assert.False(Send(RawInputEvent.KeyDown(InputKey.LeftControl)));
        Assert.True(Send(RawInputEvent.MiddleDown(10, 20)));
        Assert.False(Send(RawInputEvent.Move(30, 40)));
        Assert.True(Send(RawInputEvent.MiddleUp(50, 60)));
        Assert.False(Send(RawInputEvent.KeyUp(InputKey.LeftControl)));

        var events = Drain();
        Assert.Equal([GestureEventKind.Activated, GestureEventKind.Confirmed], events.Select(e => e.Kind).ToArray());
        Assert.Equal(new ScreenPoint(10, 20), events[0].Point);
        Assert.Equal(new ScreenPoint(50, 60), events[1].Point);
        Assert.True(events[1].Sequence > events[0].Sequence);
    }

    [Fact]
    public void Movements_AreCoalesced_AndDoNotUseTheCriticalChannel()
    {
        StartService();
        Activate();

        for (var i = 0; i < 500; i++)
        {
            Send(RawInputEvent.Move(i, i * 2));
        }

        Assert.Equal(new ScreenPoint(499, 998), _service.LatestPointer);
        Assert.True(_service.PointerSequence >= 500);
        Assert.Equal([GestureEventKind.Activated], Drain().Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void Movements_WhenIdle_UpdateThePointerButPublishNothing()
    {
        StartService();

        Send(RawInputEvent.Move(5, 6));

        Assert.Equal(new ScreenPoint(5, 6), _service.LatestPointer);
        Assert.Empty(Drain());
        Assert.True(_service.GetDiagnostics().HasPointer);
    }

    [Fact]
    public void ConfirmationCarriesThePositionAtTheMomentOfRelease_NotTheLastCoalescedMove()
    {
        StartService();
        Activate();
        Send(RawInputEvent.Move(1, 1));

        Send(RawInputEvent.MiddleUp(900, 800));

        var confirmed = Drain().Single(e => e.Kind == GestureEventKind.Confirmed);
        Assert.Equal(new ScreenPoint(900, 800), confirmed.Point);
    }

    [Fact]
    public void MiddleFirst_PassesThrough_AndPublishesNothing()
    {
        StartService();

        var down = Send(RawInputEvent.MiddleDown());
        Send(RawInputEvent.KeyDown(InputKey.LeftControl));
        var up = Send(RawInputEvent.MiddleUp());

        Assert.False(down);
        Assert.False(up);
        Assert.Empty(Drain());
    }

    [Fact]
    public void Escape_CancelsAndConsumesEscapeAndTheMiddleRelease()
    {
        StartService();
        Activate();

        Assert.True(Send(RawInputEvent.KeyDown(InputKey.Escape)));
        Assert.True(Send(RawInputEvent.KeyUp(InputKey.Escape)));
        Assert.True(Send(RawInputEvent.MiddleUp()));

        var events = Drain();
        Assert.Equal([GestureEventKind.Activated, GestureEventKind.Cancelled], events.Select(e => e.Kind).ToArray());
        Assert.Equal(CancelCause.Escape, events[1].Cause);
    }

    [Fact]
    public void CtrlRelease_Cancels_AndTheMiddleReleaseDoesNotConfirm()
    {
        StartService();
        Activate();

        Send(RawInputEvent.KeyUp(InputKey.LeftControl));
        var up = Send(RawInputEvent.MiddleUp());

        Assert.True(up);
        var events = Drain();
        Assert.DoesNotContain(events, e => e.Kind == GestureEventKind.Confirmed);
        Assert.Equal(CancelCause.CtrlReleased, events.Single(e => e.Kind == GestureEventKind.Cancelled).Cause);
    }

    [Fact]
    public void ConsecutiveGestures_KeepIncreasingSequenceNumbers()
    {
        StartService();
        Activate();
        Send(RawInputEvent.MiddleUp(1, 1));
        Send(RawInputEvent.MiddleDown(2, 2));
        Send(RawInputEvent.MiddleUp(3, 3));

        var sequences = Drain().Select(e => e.Sequence).ToArray();

        Assert.Equal(4, sequences.Length);
        Assert.Equal(sequences.OrderBy(s => s), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Length);
    }

    [Fact]
    public void InjectedInput_IsIgnored_UnlessDiagnosticModeIsEnabled_AndThenOnlyOurOwn()
    {
        StartService();
        Assert.False(_service.AcceptOwnSyntheticInput);
        Send(RawInputEvent.KeyDown(InputKey.LeftControl, injected: true));
        Assert.False(Send(RawInputEvent.MiddleDown(1, 1, injected: true)));
        Send(RawInputEvent.KeyDown(InputKey.LeftControl, ownSynthetic: true));
        Assert.False(Send(RawInputEvent.MiddleDown(1, 1, ownSynthetic: true)));
        Assert.Empty(Drain());

        _service.AcceptOwnSyntheticInput = true;

        // Otra aplicación inyectando durante la autoprueba: se sigue ignorando.
        Send(RawInputEvent.KeyDown(InputKey.LeftControl, injected: true));
        Assert.False(Send(RawInputEvent.MiddleDown(1, 1, injected: true)));
        Assert.Empty(Drain());

        // Lo que inyecta Radial con su marca: se acepta.
        Send(RawInputEvent.KeyDown(InputKey.LeftControl, ownSynthetic: true));
        var consumed = Send(RawInputEvent.MiddleDown(1, 1, ownSynthetic: true));

        Assert.True(consumed);
        Assert.Single(Drain());
    }

    // Parada y reinicio durante un gesto

    [Fact]
    public void Stop_DuringAGesture_PublishesCancelledWithTheServiceStoppedCause()
    {
        StartService();
        Activate();

        _service.Stop();

        var cancelled = Drain().Single(e => e.Kind == GestureEventKind.Cancelled);
        Assert.Equal(CancelCause.ServiceStopped, cancelled.Cause);
        Assert.Equal(CaptureState.Stopped, _service.State);
        Assert.False(_timers.Watchdog.Running);
    }

    [Fact]
    public void Restart_AfterStop_StartsFromACleanTracker()
    {
        StartService();
        Activate();
        _service.Stop();
        Drain();

        StartService();
        var diagnostics = _service.GetDiagnostics();

        Assert.Equal(ChordPhase.Idle, diagnostics.Phase);
        Assert.False(diagnostics.LeftControl);
        Assert.False(diagnostics.MiddleButton);

        Activate(7, 8);
        Assert.Equal(GestureEventKind.Activated, Drain().Single().Kind);
    }

    [Fact]
    public void Start_SeedsTheTrackerFromThePhysicalState()
    {
        _physical.Keys = new PhysicalInputState(LeftControl: true, RightControl: false, Middle: false);
        StartService();

        var consumed = Send(RawInputEvent.MiddleDown(3, 4)); // sin haber visto la pulsación de Ctrl

        Assert.True(consumed);
        Assert.Equal(GestureEventKind.Activated, Drain().Single().Kind);
    }

    [Fact]
    public void RequestCancel_CancelsAnActiveGesture_AndReportsWhetherThereWasOne()
    {
        StartService();
        Assert.False(_service.RequestCancel());

        Activate();
        Assert.True(_service.RequestCancel());

        var cancelled = Drain().Single(e => e.Kind == GestureEventKind.Cancelled);
        Assert.Equal(CancelCause.Manual, cancelled.Cause);
        Assert.True(Send(RawInputEvent.MiddleUp())); // la liberación sigue consumiéndose
    }

    // Watchdog: solo vigila Ctrl y que el hook de ratón siga entregando movimiento.
    // El botón central NO se vigila: GetAsyncKeyState no ve un evento consumido por un hook.

    [Fact]
    public void Watchdog_IsOnlyRunningWhileTheGestureIsActive()
    {
        StartService();
        Assert.False(_timers.Watchdog.Running);

        Activate();
        Assert.True(_timers.Watchdog.Running);

        Send(RawInputEvent.MiddleUp());
        Assert.False(_timers.Watchdog.Running);
    }

    [Fact]
    public void Watchdog_NeedsTwoBadSamplesBeforeCancelling_WhenCtrlIsNoLongerPressed()
    {
        StartService();
        Activate();
        _physical.Keys = new PhysicalInputState(false, false, false);

        _timers.Watchdog.Tick();
        Assert.DoesNotContain(Drain(), e => e.Kind == GestureEventKind.Cancelled);

        _timers.Watchdog.Tick();
        var cancelled = Drain().Single(e => e.Kind == GestureEventKind.Cancelled);
        Assert.Equal(CancelCause.WatchdogCtrlLost, cancelled.Cause);
    }

    [Fact]
    public void Watchdog_AGoodSampleResetsTheStreak()
    {
        StartService();
        Activate();

        _physical.Keys = default;
        _timers.Watchdog.Tick();
        _physical.Keys = new PhysicalInputState(true, false, false);
        _timers.Watchdog.Tick();
        _physical.Keys = default;
        _timers.Watchdog.Tick();

        Assert.DoesNotContain(Drain(), e => e.Kind == GestureEventKind.Cancelled);
    }

    [Fact]
    public void Watchdog_TheConsumedMiddleButtonLooksReleasedToTheOs_AndMustNotCancelAHealthyGesture()
    {
        StartService();
        Activate(100, 100);
        Send(RawInputEvent.Move(100, 100));
        _physical.Cursor = new ScreenPoint(100, 100);
        _physical.Keys = new PhysicalInputState(LeftControl: true, RightControl: false, Middle: false);

        for (var i = 0; i < 20; i++)
        {
            _timers.Watchdog.Tick();
        }

        Assert.Equal([GestureEventKind.Activated], Drain().Select(e => e.Kind).ToArray());
        Assert.Equal(ChordPhase.Active, _service.GetDiagnostics().Phase);
    }

    [Fact]
    public void Watchdog_CursorMovesButTheHookDeliversNothing_CancelsAfterTwoSamples()
    {
        StartService();
        Activate(100, 100);
        Send(RawInputEvent.Move(100, 100));
        _physical.Keys = new PhysicalInputState(true, false, false);
        _physical.Cursor = new ScreenPoint(700, 500); // el cursor real está lejos de la última posición del hook

        _timers.Watchdog.Tick();
        Assert.DoesNotContain(Drain(), e => e.Kind == GestureEventKind.Cancelled);
        _timers.Watchdog.Tick();

        var events = Drain();
        Assert.Equal(CancelCause.WatchdogMouseHookSilent, events.Single(e => e.Kind == GestureEventKind.Cancelled).Cause);
        Assert.DoesNotContain(events, e => e.Kind == GestureEventKind.Confirmed);
    }

    [Fact]
    public void Watchdog_CursorCloseToTheHookPosition_IsNotAFailure()
    {
        StartService();
        Activate(100, 100);
        Send(RawInputEvent.Move(100, 100));
        _physical.Keys = new PhysicalInputState(true, false, false);
        _physical.Cursor = new ScreenPoint(120, 90); // dentro del margen de 40 px (movimiento rápido entre muestras)

        for (var i = 0; i < 10; i++)
        {
            _timers.Watchdog.Tick();
        }

        Assert.DoesNotContain(Drain(), e => e.Kind == GestureEventKind.Cancelled);
    }

    [Fact]
    public void Watchdog_AfterCancelling_StopsAndTheMiddleReleaseIsStillConsumedWithoutConfirming()
    {
        StartService();
        Activate();
        _physical.Keys = default;
        _timers.Watchdog.Tick();
        _timers.Watchdog.Tick();

        Assert.False(_timers.Watchdog.Running);
        Assert.True(Send(RawInputEvent.MiddleUp()));
        Assert.DoesNotContain(Drain(), e => e.Kind == GestureEventKind.Confirmed);
    }

    [Fact]
    public void Watchdog_AStaleTickAfterTheGestureEnded_DoesNothing()
    {
        StartService();
        Activate();
        Send(RawInputEvent.MiddleUp());
        Drain();

        _physical.Keys = default;
        _timers.Watchdog.Tick();
        _timers.Watchdog.Tick();

        Assert.Empty(Drain());
    }

    [Fact]
    public void ReadPhysicalState_ReturnsTheReaderState_ForDiagnostics()
    {
        _physical.Keys = new PhysicalInputState(true, false, true);

        Assert.Equal(new PhysicalInputState(true, false, true), _service.ReadPhysicalState());
    }


    // Sesión y suspensión

    [Theory]
    [InlineData(false, CancelCause.SessionLocked)]
    [InlineData(true, CancelCause.Suspending)]
    public void LockOrSuspend_CancelsTheActiveGesture(bool suspend, CancelCause expected)
    {
        StartService();
        Activate();

        if (suspend)
        {
            _system.RaiseSuspending();
        }
        else
        {
            _system.RaiseLocked();
        }

        Assert.Equal(expected, Drain().Single(e => e.Kind == GestureEventKind.Cancelled).Cause);
        Assert.NotEmpty(_service.GetDiagnostics().Notices);
    }

    [Fact]
    public void Resume_DoesNotPromiseRecovery_ItOnlyLeavesANotice()
    {
        StartService();

        _system.RaiseResumed();
        _system.RaiseUnlocked();

        var notices = _service.GetDiagnostics().Notices;
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.Contains("no se reinstala", n));
        Assert.Equal(1, _host.StartCalls); // no se reinstalaron los hooks
    }

    // Sonda de salud

    [Fact]
    public void HealthProbe_CursorMovesButTheHookReceivesNothing_ReportsADegradedCapture()
    {
        StartService();
        _physical.Cursor = new ScreenPoint(10, 10);
        _timers.Health.Tick(); // referencia

        _physical.Cursor = new ScreenPoint(200, 200);
        _timers.Health.Tick();

        var diagnostics = _service.GetDiagnostics();
        Assert.Equal(MouseActivity.SilentWhileCursorMoves, diagnostics.MouseActivity);
        Assert.Equal(CaptureState.Faulted, diagnostics.State);
        Assert.Contains(diagnostics.Errors, e => e.Contains("eliminado"));
    }

    [Fact]
    public void HealthProbe_HookEventsArriving_ReportsObserved()
    {
        StartService();
        _physical.Cursor = new ScreenPoint(10, 10);
        _timers.Health.Tick();

        Send(RawInputEvent.Move(200, 200));
        _physical.Cursor = new ScreenPoint(200, 200);
        _timers.Health.Tick();

        var diagnostics = _service.GetDiagnostics();
        Assert.Equal(MouseActivity.Observed, diagnostics.MouseActivity);
        Assert.Equal(CaptureState.Running, diagnostics.State);
    }

    [Fact]
    public void HealthProbe_NothingMoves_ReportsQuiet_NotAFailure()
    {
        StartService();
        _physical.Cursor = new ScreenPoint(10, 10);
        _timers.Health.Tick();
        _timers.Health.Tick();

        Assert.Equal(MouseActivity.Quiet, _service.GetDiagnostics().MouseActivity);
        Assert.Equal(CaptureState.Running, _service.GetDiagnostics().State);
    }

    [Fact]
    public void Diagnostics_WhenStopped_DoNotClaimTheMouseIsMonitored()
    {
        Assert.Equal(MouseActivity.NotMonitored, _service.GetDiagnostics().MouseActivity);
    }

    [Fact]
    public void Diagnostics_ReflectCountsAndTheLastCancellation()
    {
        StartService();
        Activate();
        Send(RawInputEvent.MiddleUp());
        Activate();
        Send(RawInputEvent.KeyDown(InputKey.Escape));

        var diagnostics = _service.GetDiagnostics();

        Assert.Equal(2, diagnostics.Activations);
        Assert.Equal(1, diagnostics.Confirmations);
        Assert.Equal(1, diagnostics.Cancellations);
        Assert.Equal(CancelCause.Escape, diagnostics.LastCancelCause);
        Assert.True(diagnostics.HasLastEvent);
        Assert.Equal(InputEventKind.KeyDown, diagnostics.LastEventKind);
        Assert.Equal(InputKey.Escape, diagnostics.LastEventKey);
        Assert.True(diagnostics.MiddleButton);
    }
}

public class HookHealthEvaluatorTests
{
    private static readonly ScreenPoint A = new(1, 1);
    private static readonly ScreenPoint B = new(9, 9);

    [Fact]
    public void CursorMoved_WithoutNewHookCalls_IsSilent() =>
        Assert.Equal(MouseActivity.SilentWhileCursorMoves, HookHealthEvaluator.Evaluate(10, 10, A, B));

    [Fact]
    public void CursorMoved_WithNewHookCalls_IsObserved() =>
        Assert.Equal(MouseActivity.Observed, HookHealthEvaluator.Evaluate(10, 15, A, B));

    [Fact]
    public void CursorStill_WithoutHookCalls_IsQuiet() =>
        Assert.Equal(MouseActivity.Quiet, HookHealthEvaluator.Evaluate(10, 10, A, A));

    [Fact]
    public void CursorStill_WithClicks_IsObserved() =>
        Assert.Equal(MouseActivity.Observed, HookHealthEvaluator.Evaluate(10, 12, A, A));
}
