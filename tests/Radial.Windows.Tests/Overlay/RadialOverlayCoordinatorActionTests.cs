using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>El coordinador conectado a un ejecutor de acciones: qué se ejecuta, cuándo y qué nunca se ejecuta.</summary>
public class RadialOverlayCoordinatorActionTests
{
    private sealed class FakeClock : IClock
    {
        public TimeSpan Now => TimeSpan.Zero;
    }

    private sealed class FakePresenter : IRadialPresenter
    {
        public bool Visible { get; private set; }

        public event Action<PresentedPlacement>? PlacementChanged
        {
            add { }
            remove { }
        }

        public PresentedPlacement Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
        {
            Visible = true;
            return new PresentedPlacement(1.0, center);
        }

        public PresentedPlacement ShowMenu(RadialMenu menu, int depth, ScreenPoint center) => new(1.0, center);

        public PresentedPlacement FollowCursor(ScreenPoint cursor, PointD offsetDip) => new(1.0, cursor);

        public void SetProgress(RadialProgress progress)
        {
        }

        public void SetSelection(RadialDirection? direction)
        {
        }

        public void Hide() => Visible = false;
    }

    /// <summary>Ejecutor que registra lo que recibe y deja decidir al test cuándo y cómo termina.</summary>
    private sealed class FakeExecutor(FakePresenter presenter) : IActionExecutor
    {
        public List<RadialAction> Executed { get; } = [];

        public List<bool> OverlayVisibleWhenStarted { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public TaskCompletionSource<ActionExecutionResult>? Gate { get; set; }

        public Exception? Throw { get; set; }

        public ActionExecutionResult Result { get; set; } = ActionExecutionResult.Success;

        public bool CanExecute(RadialAction action) => true;

        public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
        {
            Executed.Add(action);
            OverlayVisibleWhenStarted.Add(presenter.Visible);
            Tokens.Add(cancellationToken);

            if (Throw is not null)
            {
                throw Throw;
            }

            if (Gate is { } gate)
            {
                // Termina cuando el test lo decide, o con Cancelled si se cancela el token.
                cancellationToken.Register(() => gate.TrySetResult(ActionExecutionResult.Cancelled));
                return gate.Task;
            }

            return Task.FromResult(Result);
        }
    }

    private static readonly ScreenPoint Center = new(1000, 500);
    private readonly FakePresenter _presenter = new();
    private readonly FakeExecutor _executor;
    private readonly List<(RadialItem Item, ActionExecutionResult Result)> _finished = [];
    private readonly SemaphoreSlim _signal = new(0);
    private long _sequence;
    private long _now = 1_000;
    private ScreenPoint _pointer = Center;

    public RadialOverlayCoordinatorActionTests()
    {
        _executor = new FakeExecutor(_presenter);
    }

    private static RadialConfiguration Configuration()
    {
        var root = new MenuId("main");
        var items = new[]
        {
            new RadialItem(new ItemId("copy"), "Copiar", RadialDirection.North, new SendKeysAction(new KeyChord(KeyModifiers.Control, "C"))),
            new RadialItem(new ItemId("paste"), "Pegar", RadialDirection.East, new SendKeysAction(new KeyChord(KeyModifiers.Control, "V"))),
            new RadialItem(new ItemId("web"), "Web", RadialDirection.South, new OpenUrlAction("https://example.com")),
        };

        return new RadialConfiguration
        {
            RootMenuId = root,
            Menus = [new RadialMenu(root, "Main", items)],
            Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
        };
    }

    private RadialOverlayCoordinator Create(bool withExecutor = true)
    {
        var coordinator = new RadialOverlayCoordinator(Configuration(), new FakeClock(), _presenter, () => _pointer, () => _now, withExecutor ? _executor : null);
        coordinator.ActionFinished += (item, result) =>
        {
            lock (_finished)
            {
                _finished.Add((item, result));
            }

            _signal.Release();
        };
        return coordinator;
    }

    private GestureEvent Event(GestureEventKind kind, ScreenPoint point, long timestamp, CancelCause cause = CancelCause.None) =>
        new(++_sequence, timestamp, kind, point, cause);

    private static ScreenPoint Towards(RadialDirection direction) => direction switch
    {
        RadialDirection.North => new ScreenPoint(Center.X, Center.Y - 120),
        RadialDirection.East => new ScreenPoint(Center.X + 120, Center.Y),
        RadialDirection.South => new ScreenPoint(Center.X, Center.Y + 120),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private void Gesture(RadialOverlayCoordinator coordinator, ScreenPoint releaseAt, long start = 2_000)
    {
        coordinator.Handle(Event(GestureEventKind.Activated, Center, start));
        coordinator.Handle(Event(GestureEventKind.Confirmed, releaseAt, start + 500));
    }

    // Ejecución

    [Fact]
    public void ConfirmingASector_ExecutesThatItemsAction()
    {
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.North));

        var executed = Assert.Single(_executor.Executed);
        Assert.Equal(new SendKeysAction(new KeyChord(KeyModifiers.Control, "C")), executed);
        var (item, result) = Assert.Single(_finished);
        Assert.Equal("Copiar", item.Name);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ATypeTextSelection_IsExecutedExactlyOnce_WhateverTheNumberOfPollsAndRepeatedEvents()
    {
        var root = new MenuId("main");
        var text = new TypeTextAction("Texto de prueba ñ 😀\r\nfin");
        var configuration = new RadialConfiguration
        {
            RootMenuId = root,
            Menus = [new RadialMenu(root, "Main", [new RadialItem(new ItemId("texto"), "Texto", RadialDirection.North, text)])],
            Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
        };
        var coordinator = new RadialOverlayCoordinator(configuration, new FakeClock(), _presenter, () => _pointer, () => _now, _executor);
        coordinator.ActionFinished += (item, result) =>
        {
            lock (_finished)
            {
                _finished.Add((item, result));
            }
        };

        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        _pointer = Towards(RadialDirection.North);
        for (var i = 0; i < 5; i++)
        {
            coordinator.Poll(); // el sondeo del cursor solo actualiza el resalte
        }

        coordinator.Handle(Event(GestureEventKind.Confirmed, _pointer, 2_500));
        for (var i = 0; i < 5; i++)
        {
            coordinator.Poll(); // ya no hay radial: nada que sondear ni ejecutar
        }

        coordinator.Handle(Event(GestureEventKind.Confirmed, _pointer, 2_600)); // un «confirmado» repetido no vuelve a ejecutar
        coordinator.Handle(Event(GestureEventKind.Cancelled, _pointer, 2_700, CancelCause.CtrlReleased));

        Assert.Same(text, Assert.Single(_executor.Executed));
        Assert.Single(_finished);
    }

    [Theory]
    [InlineData(RadialDirection.North, "copy")]
    [InlineData(RadialDirection.East, "paste")]
    [InlineData(RadialDirection.South, "web")]
    public void EachSector_ExecutesItsOwnItem(RadialDirection direction, string id)
    {
        var coordinator = Create();

        Gesture(coordinator, Towards(direction));

        Assert.Equal(id, Assert.Single(_finished).Item.Id.Value);
    }

    [Fact]
    public void TheOverlayIsAlreadyHidden_WhenTheActionStarts()
    {
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Equal([false], _executor.OverlayVisibleWhenStarted);
    }

    [Fact]
    public void Completed_IsRaisedBeforeTheActionStarts()
    {
        var coordinator = Create();
        var order = new List<string>();
        coordinator.Completed += _ => order.Add("completed:" + _executor.Executed.Count);
        coordinator.ActionFinished += (_, _) => order.Add("finished");

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Equal(["completed:0", "finished"], order);
    }

    [Fact]
    public void AFailureOfTheAction_IsReportedWithItsCode_AndDoesNotBreakTheCoordinator()
    {
        _executor.Result = ActionExecutionResult.Failure(ActionErrorCodes.TargetNotFound, "No se encontró la carpeta.");
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.South));

        Assert.Equal(ActionErrorCodes.TargetNotFound, Assert.Single(_finished).Result.Error!.Code);

        // Sigue funcionando para el gesto siguiente.
        _executor.Result = ActionExecutionResult.Success;
        Gesture(coordinator, Towards(RadialDirection.East), start: 5_000);
        Assert.Equal(2, _finished.Count);
        Assert.True(_finished[1].Result.IsSuccess);
    }

    [Fact]
    public void AnExecutorThatThrows_IsReportedAsAnUnexpectedError_NotLost()
    {
        _executor.Throw = new InvalidOperationException("boom");
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.North));

        var (_, result) = Assert.Single(_finished);
        Assert.Equal(ActionErrorCodes.UnexpectedError, result.Error!.Code);
        Assert.Contains("InvalidOperationException", result.Error.Message);
        Assert.DoesNotContain("boom", result.Error.Message);
    }

    // Lo que no se ejecuta

    [Fact]
    public void ConfirmingInTheCentralZone_ExecutesNothing()
    {
        var coordinator = Create();

        Gesture(coordinator, Center);

        Assert.Empty(_executor.Executed);
        Assert.Empty(_finished);
    }

    [Fact]
    public void ConfirmingOverAnEmptySector_ExecutesNothing()
    {
        var coordinator = Create();

        Gesture(coordinator, new ScreenPoint(Center.X - 120, Center.Y)); // oeste: sin elemento

        Assert.Empty(_executor.Executed);
    }

    [Theory]
    [InlineData(CancelCause.Escape)]
    [InlineData(CancelCause.CtrlReleased)]
    [InlineData(CancelCause.ServiceStopped)]
    public void ACancelledGesture_ExecutesNothing(CancelCause cause)
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        _pointer = Towards(RadialDirection.North);
        coordinator.Poll();

        coordinator.Handle(Event(GestureEventKind.Cancelled, _pointer, 2_500, cause));

        Assert.Empty(_executor.Executed);
        Assert.Empty(_finished);
    }

    [Fact]
    public void WithoutAnExecutor_ConfirmingJustReportsTheChoice()
    {
        var coordinator = Create(withExecutor: false);
        RadialResult? completed = null;
        coordinator.Completed += r => completed = r;

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Equal(RadialResultKind.Execute, completed!.Kind);
        Assert.Empty(_executor.Executed);
        Assert.Empty(_finished);
    }

    // Aislamiento durante la autoprueba

    [Fact]
    public void WhileIsolated_ASyntheticGesture_ExecutesNothingAndShowsNothing()
    {
        var coordinator = Create();
        coordinator.Isolate();

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Empty(_executor.Executed);
        Assert.Empty(_finished);
        Assert.False(_presenter.Visible);
    }

    [Fact]
    public void AfterTheSelfTest_EventsItLeftInTheChannel_AreDiscarded_AndExecuteNothing()
    {
        var coordinator = Create();
        coordinator.Isolate();
        _now = 10_000;
        coordinator.Resume();

        Gesture(coordinator, Towards(RadialDirection.North), start: 9_000); // de la autoprueba: anteriores a Resume

        Assert.Empty(_executor.Executed);
        Assert.False(_presenter.Visible);
    }

    [Fact]
    public void AfterTheSelfTest_ARealGestureExecutesAgain()
    {
        var coordinator = Create();
        coordinator.Isolate();
        _now = 10_000;
        coordinator.Resume();

        Gesture(coordinator, Towards(RadialDirection.North), start: 11_000);

        Assert.Single(_executor.Executed);
    }

    // Marca de la autoprueba en el propio evento (no depende de Isolate/Resume)

    private void SyntheticGesture(RadialOverlayCoordinator coordinator, ScreenPoint releaseAt, long start = 2_000)
    {
        coordinator.Handle(Event(GestureEventKind.Activated, Center, start) with { SyntheticWindow = true });
        coordinator.Handle(Event(GestureEventKind.Confirmed, releaseAt, start + 500) with { SyntheticWindow = true });
    }

    [Fact]
    public void AMarkedGesture_ShowsNothingAndExecutesNothing_EvenWithoutIsolating()
    {
        var coordinator = Create(); // nadie llamó a Isolate: la barrera es la marca del evento

        SyntheticGesture(coordinator, Towards(RadialDirection.North));

        Assert.False(coordinator.IsIsolated);
        Assert.False(_presenter.Visible);
        Assert.False(coordinator.IsActive);
        Assert.Empty(_executor.Executed);
        Assert.Empty(_finished);
    }

    [Fact]
    public void AMarkedGesture_ReadAfterTheIsolationWasLifted_StillExecutesNothing()
    {
        // La autoprueba terminó, se llamó a Resume con una marca de tiempo anterior a los eventos (reloj atrasado a propósito),
        // pero los eventos llevan la marca: no se ejecuta nada.
        var coordinator = Create();
        coordinator.Isolate();
        _now = 100; // Resume fija «ignorar antes de 100»: los eventos de abajo (2000+) pasarían el filtro de tiempo
        coordinator.Resume();

        SyntheticGesture(coordinator, Towards(RadialDirection.North));

        Assert.Empty(_executor.Executed);
        Assert.False(_presenter.Visible);
    }

    [Fact]
    public void AMarkedCancellation_DoesNotAbortARealGestureInProgress()
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000)); // real
        var aborted = 0;
        coordinator.Aborted += _ => aborted++;

        coordinator.Handle(Event(GestureEventKind.Cancelled, Center, 2_100, CancelCause.ServiceStopped) with { SyntheticWindow = true });

        Assert.True(coordinator.IsActive);
        Assert.Equal(0, aborted);
    }

    [Fact]
    public void ARealConfirmation_ThatFollowsAMarkedActivation_ExecutesNothing()
    {
        // El gesto físico empezó durante la autoprueba (su activación lleva la marca) y termina después: nunca hubo radial.
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000) with { SyntheticWindow = true });

        coordinator.Handle(Event(GestureEventKind.Confirmed, Towards(RadialDirection.North), 2_500));

        Assert.Empty(_executor.Executed);
        Assert.False(_presenter.Visible);
    }

    [Fact]
    public void AnUnmarkedGesture_StillExecutes()
    {
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Single(_executor.Executed);
    }

    // Acciones pendientes

    [Fact]
    public async Task ASlowAction_DoesNotBlock_AndIsPendingUntilItFinishes()
    {
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.North)); // vuelve aunque la acción siga esperando

        Assert.True(coordinator.HasPendingAction);
        Assert.Empty(_finished);

        _executor.Gate.SetResult(ActionExecutionResult.Success);
        await WaitForFinishedAsync();

        Assert.False(coordinator.HasPendingAction);
        Assert.True(Assert.Single(_finished).Result.IsSuccess);
    }

    [Fact]
    public async Task IsolatingWhileAnActionWaits_CancelsIt()
    {
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();
        Gesture(coordinator, Towards(RadialDirection.North));

        coordinator.Isolate();
        await WaitForFinishedAsync();

        Assert.True(_executor.Tokens.Single().IsCancellationRequested);
        Assert.Equal(ActionExecutionStatus.Cancelled, Assert.Single(_finished).Result.Status);
        Assert.False(coordinator.HasPendingAction);
    }

    [Fact]
    public async Task AnewGesture_CancelsTheActionThatWasStillWaiting()
    {
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();
        Gesture(coordinator, Towards(RadialDirection.North));
        var firstToken = _executor.Tokens.Single();

        _executor.Gate = null;
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 5_000));
        await WaitForFinishedAsync();

        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(ActionExecutionStatus.Cancelled, Assert.Single(_finished).Result.Status);
    }

    /// <summary>La continuación de una acción asíncrona puede volver por el contexto de sincronización de xUnit: se espera la señal.</summary>
    private async Task WaitForFinishedAsync() => Assert.True(await _signal.WaitAsync(TimeSpan.FromSeconds(5)), "La acción no terminó.");

    [Fact]
    public void Dismissing_CancelsAPendingAction()
    {
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();
        Gesture(coordinator, Towards(RadialDirection.North));

        coordinator.Dismiss();

        Assert.True(_executor.Tokens.Single().IsCancellationRequested);
    }

    [Fact]
    public void TheCancellationToken_IsNotCancelledWhileTheActionRuns()
    {
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.False(_executor.Tokens.Single().IsCancellationRequested);
        Assert.True(coordinator.HasPendingAction);
    }

    // Hilo de entrega (el contrato que la ventana necesita para tocar la interfaz)

    [Fact]
    public async Task ActionFinished_IsDeliveredOnTheContextThatStartedTheAction_EvenIfTheExecutorFinishesElsewhere()
    {
        using var context = new SingleThreadContext(); // hace de hilo de interfaz
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();
        var deliveredOn = new List<int>();
        var delivered = new SemaphoreSlim(0);
        coordinator.ActionFinished += (_, _) =>
        {
            deliveredOn.Add(Environment.CurrentManagedThreadId);
            delivered.Release();
        };

        await context.InvokeAsync(() => Gesture(coordinator, Towards(RadialDirection.North))); // la acción empieza en «el hilo de interfaz»
        _executor.Gate.SetResult(ActionExecutionResult.Success); // y termina desde otro hilo

        Assert.True(await delivered.WaitAsync(TimeSpan.FromSeconds(5)), "ActionFinished no llegó.");
        Assert.Equal([context.ThreadId], deliveredOn);
        Assert.Empty(context.Faults);
    }

    [Fact]
    public async Task TheStateOfTheCoordinator_IsOnlyTouchedFromTheContext_WhileTheActionFinishes()
    {
        // La limpieza de la acción pendiente (HasPendingAction) también ocurre en el contexto, no en el hilo que completó el ejecutor.
        using var context = new SingleThreadContext();
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create();
        var pendingSeenByTheSubscriber = new List<bool>();
        var delivered = new SemaphoreSlim(0);
        coordinator.ActionFinished += (_, _) =>
        {
            pendingSeenByTheSubscriber.Add(coordinator.HasPendingAction);
            delivered.Release();
        };
        await context.InvokeAsync(() => Gesture(coordinator, Towards(RadialDirection.North)));

        _executor.Gate.SetResult(ActionExecutionResult.Success);
        Assert.True(await delivered.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal([false], pendingSeenByTheSubscriber); // cuando el suscriptor se entera, la acción ya no está pendiente
    }

    // Suscriptores defectuosos

    private static void Boom(string text) => throw new InvalidOperationException(text);

    [Fact]
    public void AFaultyCompletedSubscriber_DoesNotPreventTheConfirmedActionFromRunning()
    {
        var coordinator = Create();
        coordinator.Completed += _ => Boom("suscriptor roto");

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Equal(new SendKeysAction(new KeyChord(KeyModifiers.Control, "C")), Assert.Single(_executor.Executed));
        Assert.True(Assert.Single(_finished).Result.IsSuccess);
        Assert.False(_presenter.Visible);
    }

    [Fact]
    public void AFaultyCompletedSubscriber_DoesNotStopTheOnesAfterIt_AndKeepsTheLogicalOrder()
    {
        var coordinator = Create();
        var order = new List<string>();
        coordinator.Completed += _ => order.Add("completed-1");
        coordinator.Completed += _ => Boom("roto");
        coordinator.Completed += _ => order.Add("completed-3:executed=" + _executor.Executed.Count);
        coordinator.ActionFinished += (_, _) => order.Add("finished");

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Equal(["completed-1", "completed-3:executed=0", "finished"], order);
    }

    [Fact]
    public void AFaultyCompletedSubscriber_IsReported_WithTheEventNameAndTheException()
    {
        var coordinator = Create();
        var failures = new List<(string Event, Exception Exception)>();
        coordinator.SubscriberFailed += (name, exception) => failures.Add((name, exception));
        coordinator.Completed += _ => Boom("suscriptor roto");

        Gesture(coordinator, Towards(RadialDirection.North));

        var (name, exception) = Assert.Single(failures);
        Assert.Equal("Completed", name);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("suscriptor roto", exception.Message);
    }

    [Fact]
    public void AFaultyCompletedSubscriber_WithoutAnExecutor_DoesNotBreakHandle()
    {
        var coordinator = Create(withExecutor: false);
        var failures = new List<string>();
        coordinator.SubscriberFailed += (name, _) => failures.Add(name);
        coordinator.Completed += _ => Boom("roto");

        Gesture(coordinator, Towards(RadialDirection.North)); // no debe lanzar

        Assert.Equal(["Completed"], failures);
        Assert.False(coordinator.IsActive);
    }

    [Fact]
    public async Task AFaultyActionFinishedSubscriber_DoesNotStopTheOnesAfterIt_NorLeavesTheActionPending()
    {
        _executor.Gate = new TaskCompletionSource<ActionExecutionResult>();
        var coordinator = Create(); // su suscriptor señala el final
        var failures = new List<(string Event, Exception Exception)>();
        var received = new List<ActionExecutionStatus>();
        coordinator.SubscriberFailed += (name, exception) => failures.Add((name, exception));
        coordinator.ActionFinished += (_, _) => Boom("roto");
        var lastSubscriberDone = new SemaphoreSlim(0);
        coordinator.ActionFinished += (_, result) =>
        {
            received.Add(result.Status);
            lastSubscriberDone.Release();
        };
        Gesture(coordinator, Towards(RadialDirection.North));

        _executor.Gate.SetResult(ActionExecutionResult.Success);
        Assert.True(await lastSubscriberDone.WaitAsync(TimeSpan.FromSeconds(5)), "El último suscriptor no recibió el resultado.");

        // El suscriptor roto está entre dos buenos: el fallo se comunica, no sale de la tarea (no queda sin observar) y los demás reciben el resultado.
        Assert.Equal([ActionExecutionStatus.Succeeded], received);
        var (name, exception) = Assert.Single(failures);
        Assert.Equal("ActionFinished", name);
        Assert.Equal("roto", exception.Message);
        Assert.False(coordinator.HasPendingAction);
    }

    [Fact]
    public void AFaultyActionFinishedSubscriber_DoesNotHideAnExecutorFailure()
    {
        _executor.Throw = new InvalidOperationException("boom del ejecutor");
        var coordinator = Create();
        var received = new List<ActionExecutionResult>();
        var failures = new List<string>();
        coordinator.SubscriberFailed += (name, _) => failures.Add(name);
        coordinator.ActionFinished += (_, _) => Boom("suscriptor roto");
        coordinator.ActionFinished += (_, result) => received.Add(result);

        Gesture(coordinator, Towards(RadialDirection.North)); // el ejecutor lanza al llamarlo: todo termina de forma síncrona

        // El fallo del ejecutor sigue llegando como UnexpectedError y el del suscriptor se comunica aparte, sin confundirlos.
        Assert.Equal(ActionErrorCodes.UnexpectedError, Assert.Single(received).Error!.Code);
        Assert.Contains("InvalidOperationException", received[0].Error!.Message);
        Assert.Equal(["ActionFinished"], failures);
    }

    [Fact]
    public void ASubscriberFailedHandlerThatAlsoThrows_IsIgnored_AndTheActionStillRuns()
    {
        var coordinator = Create();
        coordinator.SubscriberFailed += (_, _) => Boom("el aviso también falla");
        coordinator.Completed += _ => Boom("roto");

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Single(_executor.Executed);
        Assert.Single(_finished);
    }

    [Fact]
    public void AFaultyAbortedSubscriber_CannotPreventHidingTheOverlayOrReleasingTheState()
    {
        var coordinator = Create();
        var reported = new List<string>();
        var received = new List<CancelCause>();
        coordinator.SubscriberFailed += (name, _) => reported.Add(name);
        coordinator.Aborted += _ => Boom("roto");
        coordinator.Aborted += cause => received.Add(cause);
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        coordinator.Handle(Event(GestureEventKind.Cancelled, Center, 2_100, CancelCause.Escape)); // no debe lanzar

        Assert.False(_presenter.Visible);
        Assert.False(coordinator.IsActive);
        Assert.Equal([CancelCause.Escape], received); // el suscriptor de después sí lo recibe
        Assert.Equal(["Aborted"], reported);
        Assert.Empty(_executor.Executed);
    }

    [Fact]
    public void AfterAFaultyAbortedSubscriber_TheNextGestureStillExecutes()
    {
        var coordinator = Create();
        coordinator.Aborted += _ => Boom("roto");
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        coordinator.Handle(Event(GestureEventKind.Cancelled, Center, 2_100, CancelCause.CtrlReleased));

        Gesture(coordinator, Towards(RadialDirection.North), start: 5_000);

        Assert.Single(_executor.Executed);
    }

    [Fact]
    public void WithoutFaultySubscribers_NothingIsReported()
    {
        var coordinator = Create();
        var failures = 0;
        coordinator.SubscriberFailed += (_, _) => failures++;
        coordinator.Completed += _ => { };

        Gesture(coordinator, Towards(RadialDirection.North));

        Assert.Equal(0, failures);
    }
}
