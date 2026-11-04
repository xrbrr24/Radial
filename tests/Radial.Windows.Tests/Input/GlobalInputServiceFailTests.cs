using Radial.Windows.Input;

namespace Radial.Windows.Tests.Input;

/// <summary>
/// <c>GlobalInputService.Fail</c>: la captura se detiene como fallo (hooks fuera, gesto cancelado, estado «con problemas») cuando
/// quien consume los eventos deja de funcionar. Con hooks, temporizadores y eventos del sistema falsos.
/// </summary>
public class GlobalInputServiceFailTests : IDisposable
{
    private readonly FakeHookHost _host = new();
    private readonly FakePhysicalReader _physical = new();
    private readonly FakeTimerFactory _timers = new();
    private readonly FakeSystemEvents _system = new();
    private readonly GlobalInputService _service;

    public GlobalInputServiceFailTests()
    {
        _service = new GlobalInputService(_host, _physical, _timers, _system);
    }

    public void Dispose() => _service.Dispose();

    private void StartService()
    {
        var result = _service.Start();
        Assert.True(result.Success, result.Message);
    }

    private List<GestureEvent> Drain()
    {
        var events = new List<GestureEvent>();
        while (_service.Events.TryRead(out var e))
        {
            events.Add(e);
        }

        return events;
    }

    [Fact]
    public void Fail_WhileRunning_ReleasesTheHooks_AndLeavesTheServiceFaulted()
    {
        StartService();

        _service.Fail("el lector murió");

        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(1, _host.StopCalls);
        Assert.False(_host.IsRunning);
        Assert.Equal(1, _system.DetachCalls);
        Assert.False(_timers.Health.Running);

        var diagnostics = _service.GetDiagnostics();
        Assert.Equal(CaptureState.Faulted, diagnostics.State);
        Assert.False(diagnostics.KeyboardHook.Installed);
        Assert.False(diagnostics.MouseHook.Installed);
        Assert.Contains(diagnostics.Errors, e => e.Contains("el lector murió"));
    }

    [Fact]
    public void Fail_WithAnActiveGesture_CancelsIt()
    {
        StartService();
        _host.Send(RawInputEvent.KeyDown(InputKey.LeftControl));
        Assert.True(_host.Send(RawInputEvent.MiddleDown(100, 100)));
        Assert.Equal(ChordPhase.Active, _service.GetDiagnostics().Phase);

        _service.Fail("el lector murió");

        var diagnostics = _service.GetDiagnostics();
        Assert.Equal(ChordPhase.Idle, diagnostics.Phase);
        Assert.Equal(1, diagnostics.Activations);
        Assert.Equal(1, diagnostics.Cancellations);
        Assert.Equal(CancelCause.ServiceStopped, diagnostics.LastCancelCause);
        Assert.Equal([GestureEventKind.Activated, GestureEventKind.Cancelled], Drain().Select(e => e.Kind).ToArray());
        Assert.False(_timers.Watchdog.Running);
    }

    [Fact]
    public void Fail_CalledTwice_OnlyActsOnce()
    {
        StartService();

        _service.Fail("primero");
        _service.Fail("segundo");

        Assert.Equal(1, _host.StopCalls);
        Assert.Equal(1, _system.DetachCalls);
        var errors = _service.GetDiagnostics().Errors;
        Assert.Single(errors, e => e.Contains("fallo interno"));
        Assert.DoesNotContain(errors, e => e.Contains("segundo"));
    }

    [Fact]
    public void Fail_WhenTheCaptureWasAlreadyStopped_MarksItFaulted_WithoutTouchingTheHooksAgain()
    {
        StartService();
        _service.Stop();
        Assert.Equal(1, _host.StopCalls);

        _service.Fail("el lector murió");

        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(1, _host.StopCalls);
    }

    [Fact]
    public void Fail_WhenNeverStarted_MarksItFaulted_AndInstallsNothing()
    {
        _service.Fail("el lector murió");

        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(0, _host.StartCalls);
        Assert.Equal(0, _host.StopCalls);
    }

    [Fact]
    public void Fail_AfterDispose_DoesNothing()
    {
        StartService();
        _service.Dispose();
        Assert.Equal(1, _host.StopCalls);

        _service.Fail("el lector murió");

        Assert.Equal(1, _host.StopCalls);
        Assert.Equal(CaptureState.Stopped, _service.State);
    }

    [Fact]
    public void Dispose_AfterFail_DoesNotReleaseTheHooksAgain()
    {
        StartService();
        _service.Fail("el lector murió");

        _service.Dispose();

        Assert.Equal(1, _host.StopCalls);
        Assert.True(_service.Events.Completion.IsCompleted);
    }

    [Fact]
    public void Stop_AfterFail_DoesNotRelease_NorEraseTheFailure()
    {
        StartService();
        _service.Fail("el lector murió");

        _service.Stop();

        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(1, _host.StopCalls);
    }

    [Fact]
    public void Start_AfterFail_StartsAgain_AndALaterFailureIsHandledAgain()
    {
        StartService();
        _service.Fail("primero");

        StartService();
        Assert.Equal(CaptureState.Running, _service.State);
        Assert.True(_host.IsRunning);

        _service.Fail("segundo");

        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(2, _host.StopCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_WithoutAReason_IsRejected(string reason)
    {
        StartService();

        Assert.Throws<ArgumentException>(() => _service.Fail(reason));

        Assert.Equal(CaptureState.Running, _service.State);
    }
}
