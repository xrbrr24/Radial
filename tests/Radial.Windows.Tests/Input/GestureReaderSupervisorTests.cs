using System.Threading.Channels;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Input;

/// <summary>
/// Fallo seguro del ciclo de vida de la captura: si el lector de eventos termina sin que se pidiera, la captura se detiene como
/// fallo (hooks fuera, gesto cancelado, overlay oculto, estado «con problemas») y se comunica una sola vez; el cierre normal no es
/// un fallo. Con servicio de captura, coordinador y presentador reales sobre hooks falsos y un canal propio para controlar el final
/// del lector.
/// </summary>
public class GestureReaderSupervisorTests : IDisposable
{
    private sealed class FakeClock : IClock
    {
        public TimeSpan Now => TimeSpan.Zero;
    }

    private sealed class FakePresenter : IRadialPresenter
    {
        public bool Visible { get; private set; }

        public int Hides { get; private set; }

        public double Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
        {
            Visible = true;
            return 1.0;
        }

        public void SetSelection(RadialDirection? direction)
        {
        }

        public void Hide()
        {
            Visible = false;
            Hides++;
        }
    }

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private readonly FakeHookHost _host = new();
    private readonly FakeTimerFactory _timers = new();
    private readonly GlobalInputService _service;
    private readonly FakePresenter _presenter = new();
    private readonly RadialOverlayCoordinator _coordinator;
    private readonly Channel<GestureEvent> _channel = Channel.CreateUnbounded<GestureEvent>();
    private readonly List<CaptureFailure> _failures = [];
    /// <summary>Lo que el coordinador comunicaría de un gesto terminado con normalidad: no debe aparecer en un fallo del lector.</summary>
    private readonly List<string> _finished = [];
    private bool _throwInTheHandler;


    public GestureReaderSupervisorTests()
    {
        _service = new GlobalInputService(_host, new FakePhysicalReader(), _timers, new FakeSystemEvents());
        _coordinator = new RadialOverlayCoordinator(Configuration(), new FakeClock(), _presenter, () => _service.LatestPointer);
        _coordinator.Completed += _ => _finished.Add("Completed");
        _coordinator.Aborted += _ => _finished.Add("Aborted");
    }

    public void Dispose() => _service.Dispose();

    private static RadialConfiguration Configuration()
    {
        var root = new MenuId("main");
        var items = Enum.GetValues<RadialDirection>()
            .Select(d => new RadialItem(new ItemId("item-" + d), d.ToString(), d, new OpenFolderAction(@"C:\Temp")))
            .ToList();

        return new RadialConfiguration
        {
            RootMenuId = root,
            Menus = [new RadialMenu(root, "Main", items)],
            Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
        };
    }

    private GestureReaderSupervisor CreateSupervisor(int maxConsecutiveFailures = 2)
    {
        var reader = new GestureEventReader(
            _channel.Reader,
            _ =>
            {
                if (_throwInTheHandler)
                {
                    throw new InvalidOperationException("fallo del manejador");
                }
            },
            maxConsecutiveFailures: maxConsecutiveFailures);
        var supervisor = new GestureReaderSupervisor(reader, _service.Fail, _coordinator.Dismiss);
        supervisor.Failed += _failures.Add;
        return supervisor;
    }

    private void StartCapture()
    {
        var result = _service.Start();
        Assert.True(result.Success, result.Message);
    }

    /// <summary>Gesto activo de verdad: el servicio lo tiene en curso y el overlay está a la vista.</summary>
    private void StartCaptureWithAnActiveGesture()
    {
        StartCapture();
        _host.Send(RawInputEvent.KeyDown(InputKey.LeftControl));
        Assert.True(_host.Send(RawInputEvent.MiddleDown(100, 100)));
        _coordinator.Handle(new GestureEvent(1, 1_000, GestureEventKind.Activated, new ScreenPoint(100, 100), CancelCause.None));
        Assert.True(_coordinator.IsActive);
        Assert.True(_presenter.Visible);
        Assert.Equal(ChordPhase.Active, _service.GetDiagnostics().Phase);
    }

    private void AssertCaptureStopped()
    {
        Assert.Equal(CaptureState.Faulted, _service.State);
        Assert.Equal(CaptureState.Faulted, _service.GetDiagnostics().State);
        Assert.False(_host.IsRunning);
        Assert.Equal(1, _host.StopCalls);
    }

    // Final inesperado

    [Fact]
    public async Task TheReaderFailing_WithoutAGesture_StopsTheCapture_AsAFailure()
    {
        StartCapture();
        var supervisor = CreateSupervisor();
        _channel.Writer.TryComplete(new InvalidOperationException("canal roto"));

        var end = await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.Equal(GestureReaderEndReason.ChannelFaulted, end.Reason);
        AssertCaptureStopped();
        var failure = Assert.Single(_failures);
        Assert.Same(failure, supervisor.Failure);
        Assert.Equal(end, failure.ReaderEnd);
        Assert.Empty(failure.CleanupProblems);
        Assert.Contains(_service.GetDiagnostics().Errors, e => e.Contains("lector de eventos"));
        Assert.False(_coordinator.IsActive);
        Assert.Equal(0, _presenter.Hides); // no había nada que ocultar
    }

    [Fact]
    public async Task TheReaderFailing_DuringAnActiveGesture_CancelsTheGesture_AndHidesTheOverlay()
    {
        StartCaptureWithAnActiveGesture();
        var supervisor = CreateSupervisor();
        _channel.Writer.TryComplete(new InvalidOperationException("canal roto"));

        await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        AssertCaptureStopped();
        Assert.False(_presenter.Visible);
        Assert.Equal(1, _presenter.Hides);
        Assert.False(_coordinator.IsActive);
        Assert.Empty(_finished); // el overlay se abandona sin informar de un gesto «confirmado» ni «cancelado»
        var diagnostics = _service.GetDiagnostics();
        Assert.Equal(ChordPhase.Idle, diagnostics.Phase);
        Assert.Equal(1, diagnostics.Cancellations);
        Assert.Equal(CancelCause.ServiceStopped, diagnostics.LastCancelCause);
        Assert.Single(_failures);
    }

    [Fact]
    public async Task TheHandlerFailingTooManyTimes_StopsTheCapture_AsAFailure()
    {
        StartCaptureWithAnActiveGesture();
        _throwInTheHandler = true;
        var supervisor = CreateSupervisor(maxConsecutiveFailures: 2);
        _channel.Writer.TryWrite(new GestureEvent(1, 1_000, GestureEventKind.Confirmed, default, CancelCause.None));
        _channel.Writer.TryWrite(new GestureEvent(2, 1_001, GestureEventKind.Confirmed, default, CancelCause.None));

        var end = await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.Equal(GestureReaderEndReason.TooManyFailures, end.Reason);
        AssertCaptureStopped();
        Assert.False(_presenter.Visible);
        Assert.Single(_failures);
    }

    [Fact]
    public async Task TheChannelClosingByItself_WithoutBeingAskedTo_IsAFailure()
    {
        StartCapture();
        var supervisor = CreateSupervisor();
        _channel.Writer.TryComplete();

        var end = await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.Equal(GestureReaderEndReason.ChannelCompleted, end.Reason);
        AssertCaptureStopped();
        Assert.Single(_failures);
    }

    [Fact]
    public async Task AfterTheFailure_TheFailureIsHandledOnlyOnce()
    {
        StartCapture();
        var supervisor = CreateSupervisor();
        _channel.Writer.TryComplete(new InvalidOperationException("canal roto"));

        await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);
        await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);
        _service.Fail("otra vez");

        Assert.Single(_failures);
        Assert.Equal(1, _host.StopCalls);
        Assert.Equal(CaptureState.Faulted, _service.State);
    }

    [Fact]
    public async Task AfterTheFailure_DisposingTheService_DoesNotReleaseTheHooksAgain()
    {
        StartCapture();
        var supervisor = CreateSupervisor();
        _channel.Writer.TryComplete(new InvalidOperationException("canal roto"));
        await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        _service.Dispose();

        Assert.Equal(1, _host.StopCalls);
        Assert.Single(_failures);
    }

    // Cierre normal

    [Fact]
    public async Task ClosingTheApplication_IsNotAFailure_AndLeavesTheCaptureAlone()
    {
        StartCaptureWithAnActiveGesture();
        var supervisor = CreateSupervisor();
        using var closing = new CancellationTokenSource();
        var run = supervisor.RunAsync(closing.Token);

        closing.Cancel();
        var end = await run.WaitAsync(Limit);

        Assert.Equal(GestureReaderEndReason.Cancelled, end.Reason);
        Assert.Empty(_failures);
        Assert.Null(supervisor.Failure);
        Assert.Equal(end, supervisor.End);
        Assert.Equal(CaptureState.Running, _service.State); // la limpieza del cierre es del propietario, no del supervisor
        Assert.Equal(0, _host.StopCalls);
        Assert.True(_presenter.Visible);
    }

    [Fact]
    public async Task ClosingTheApplication_WhileTheChannelEndsAtTheSameTime_IsNotAFailure()
    {
        StartCapture();
        var supervisor = CreateSupervisor();
        using var closing = new CancellationTokenSource();
        closing.Cancel();
        _channel.Writer.TryComplete(new InvalidOperationException("canal cerrado por la liberación del servicio"));

        await supervisor.RunAsync(closing.Token).WaitAsync(Limit);

        Assert.Empty(_failures);
        Assert.Null(supervisor.Failure);
        Assert.Equal(CaptureState.Running, _service.State);
        Assert.Equal(0, _host.StopCalls);
    }

    // Limpieza y avisos

    [Fact]
    public async Task EachCleanupStepIsAttempted_EvenIfTheOtherFails()
    {
        var channel = Channel.CreateUnbounded<GestureEvent>();
        channel.Writer.TryComplete();
        var reader = new GestureEventReader(channel.Reader, _ => { });
        var dismissed = 0;
        var supervisor = new GestureReaderSupervisor(reader, _ => throw new InvalidOperationException("hooks rotos"), () => dismissed++);

        await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.Equal(1, dismissed);
        var problem = Assert.Single(supervisor.Failure!.CleanupProblems);
        Assert.Contains("hooks", problem);

        var failed = 0;
        var otherChannel = Channel.CreateUnbounded<GestureEvent>();
        otherChannel.Writer.TryComplete();
        var otherSupervisor = new GestureReaderSupervisor(
            new GestureEventReader(otherChannel.Reader, _ => { }),
            _ => failed++,
            () => throw new InvalidOperationException("overlay roto"));

        await otherSupervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.Equal(1, failed);
        Assert.Contains("overlay", Assert.Single(otherSupervisor.Failure!.CleanupProblems));
    }

    [Fact]
    public async Task ASubscriberThatThrows_DoesNotBreakTheSupervisor_NorHideTheFailureFromTheOthers()
    {
        StartCapture();
        var supervisor = CreateSupervisor();
        var received = new List<CaptureFailure>();
        supervisor.Failed += _ => throw new InvalidOperationException("suscriptor defectuoso");
        supervisor.Failed += received.Add;
        _channel.Writer.TryComplete(new InvalidOperationException("canal roto"));

        await supervisor.RunAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.Single(received);
        Assert.Single(_failures);
        AssertCaptureStopped();
    }

    [Fact]
    public void Describe_ExplainsEachUnexpectedEnd()
    {
        Assert.Contains("cerró", GestureReaderSupervisor.Describe(new GestureReaderEnd(GestureReaderEndReason.ChannelCompleted)));
        Assert.Contains("InvalidOperationException", GestureReaderSupervisor.Describe(new GestureReaderEnd(GestureReaderEndReason.ChannelFaulted, new InvalidOperationException())));
        Assert.Contains("3 eventos", GestureReaderSupervisor.Describe(new GestureReaderEnd(GestureReaderEndReason.TooManyFailures, new InvalidOperationException(), 3)));
    }
}
