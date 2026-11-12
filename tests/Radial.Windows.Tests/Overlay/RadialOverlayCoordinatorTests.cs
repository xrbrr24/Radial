using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Core.Validation;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

public class RadialOverlayCoordinatorTests
{
    private sealed class FakeClock : IClock
    {
        public TimeSpan Now { get; set; }
    }

    private sealed class FakePresenter : IRadialPresenter
    {
        public double Scale { get; set; } = 1.0;

        public bool Visible { get; private set; }

        public event Action<PresentedPlacement>? PlacementChanged
        {
            add { }
            remove { }
        }

        public int Shows { get; private set; }

        public int Hides { get; private set; }

        public ScreenPoint? LastCenter { get; private set; }

        public RadialMenu? LastMenu { get; private set; }

        public List<RadialDirection?> Selections { get; } = [];

        public Exception? FailOnShow { get; set; }

        public PresentedPlacement Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
        {
            Visible = true;
            Shows++;
            LastMenu = menu;
            LastCenter = center;
            if (FailOnShow is not null)
            {
                throw FailOnShow;
            }

            return new PresentedPlacement(Scale, center);
        }

        public PresentedPlacement ShowMenu(RadialMenu menu, int depth, ScreenPoint center) => new(1.0, center);

        public PresentedPlacement FollowCursor(ScreenPoint cursor, PointD offsetDip) => new(1.0, cursor);

        public void SetProgress(RadialProgress progress)
        {
        }

        public void SetSelection(RadialDirection? direction) => Selections.Add(direction);

        public void Hide()
        {
            Visible = false;
            Hides++;
        }
    }

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

    private readonly FakePresenter _presenter = new();
    private ScreenPoint _pointer = new(1000, 500);
    private long _now = 1_000;

    private RadialOverlayCoordinator Create() =>
        new(Configuration(), new FakeClock(), _presenter, () => _pointer, () => _now);

    private long _sequence;

    private GestureEvent Event(GestureEventKind kind, ScreenPoint point, long timestamp, CancelCause cause = CancelCause.None) =>
        new(++_sequence, timestamp, kind, point, cause);

    private static readonly ScreenPoint Center = new(1000, 500);

    // Activación

    [Fact]
    public void ActivatingTheGesture_ShowsTheRootMenu_CenteredOnTheCursor()
    {
        var coordinator = Create();

        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        Assert.True(coordinator.IsActive);
        Assert.True(_presenter.Visible);
        Assert.Equal(Center, _presenter.LastCenter);
        Assert.Equal(8, _presenter.LastMenu!.Items.Count);
        Assert.Equal(new MenuId("main"), _presenter.LastMenu.Id);
    }

    [Fact]
    public void RightAfterActivating_NothingIsSelected()
    {
        var coordinator = Create();

        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        Assert.Empty(_presenter.Selections); // la selección parte de «ninguna»; no se notifica un cambio inexistente
    }

    // Selección

    [Theory]
    [InlineData(0, -100, RadialDirection.North)]
    [InlineData(100, -100, RadialDirection.NorthEast)]
    [InlineData(100, 0, RadialDirection.East)]
    [InlineData(100, 100, RadialDirection.SouthEast)]
    [InlineData(0, 100, RadialDirection.South)]
    [InlineData(-100, 100, RadialDirection.SouthWest)]
    [InlineData(-100, 0, RadialDirection.West)]
    [InlineData(-100, -100, RadialDirection.NorthWest)]
    public void MovingTheCursor_HighlightsTheSectorInThatDirection(int dx, int dy, RadialDirection expected)
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        _pointer = new ScreenPoint(Center.X + dx, Center.Y + dy);
        coordinator.Poll();

        Assert.Equal(expected, _presenter.Selections[^1]);
    }

    [Fact]
    public void TheSelection_IsOnlyNotifiedWhenItChanges()
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        _pointer = new ScreenPoint(Center.X + 100, Center.Y);
        coordinator.Poll();
        coordinator.Poll();
        _pointer = new ScreenPoint(Center.X + 110, Center.Y + 5); // sigue en el este
        coordinator.Poll();

        Assert.Equal([RadialDirection.East], _presenter.Selections);
    }

    [Fact]
    public void BackInTheCentralZone_TheHighlightIsRemoved()
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        _pointer = new ScreenPoint(Center.X, Center.Y - 100);
        coordinator.Poll();

        _pointer = new ScreenPoint(Center.X + 10, Center.Y); // dentro de la zona central (40)
        coordinator.Poll();

        Assert.Equal([RadialDirection.North, null], _presenter.Selections);
    }

    [Fact]
    public void ThePointerIsMeasuredInDip_SoTheDeadZoneScalesWithTheDpi()
    {
        // Al 200 % una zona central de 40 DIP son 80 píxeles físicos.
        _presenter.Scale = 2.0;
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        _pointer = new ScreenPoint(Center.X + 70, Center.Y); // 35 DIP: dentro
        coordinator.Poll();
        Assert.Empty(_presenter.Selections);

        _pointer = new ScreenPoint(Center.X + 90, Center.Y); // 45 DIP: fuera
        coordinator.Poll();
        Assert.Equal([RadialDirection.East], _presenter.Selections);
    }

    [Fact]
    public void ACursorThatMovedBeforeTheOverlayWasShown_IsPickedUpImmediately()
    {
        _pointer = new ScreenPoint(Center.X, Center.Y + 120);
        var coordinator = Create();

        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        Assert.Equal([RadialDirection.South], _presenter.Selections);
    }

    // Final del gesto

    [Fact]
    public void ConfirmingOverASector_HidesTheOverlay_AndReportsTheItem_WithoutExecutingAnything()
    {
        var coordinator = Create();
        RadialResult? completed = null;
        coordinator.Completed += r => completed = r;
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        coordinator.Handle(Event(GestureEventKind.Confirmed, new ScreenPoint(Center.X + 100, Center.Y), 3_000));

        Assert.False(coordinator.IsActive);
        Assert.False(_presenter.Visible);
        Assert.Equal(1, _presenter.Hides);
        Assert.Equal(RadialResultKind.Execute, completed!.Kind);
        Assert.Equal(RadialDirection.East, completed.Item!.Position);
    }

    [Fact]
    public void TheConfirmation_UsesThePositionOfTheReleaseEvent_NotTheLastPolledOne()
    {
        var coordinator = Create();
        RadialResult? completed = null;
        coordinator.Completed += r => completed = r;
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        _pointer = new ScreenPoint(Center.X, Center.Y - 100);
        coordinator.Poll(); // norte

        coordinator.Handle(Event(GestureEventKind.Confirmed, new ScreenPoint(Center.X - 100, Center.Y), 3_000)); // soltó al oeste

        Assert.Equal(RadialDirection.West, completed!.Item!.Position);
    }

    [Fact]
    public void ConfirmingInTheCentralZone_HidesTheOverlay_AndExecutesNothing()
    {
        var coordinator = Create();
        RadialResult? completed = null;
        coordinator.Completed += r => completed = r;
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        coordinator.Handle(Event(GestureEventKind.Confirmed, Center, 3_000));

        Assert.False(_presenter.Visible);
        Assert.Equal(RadialResultKind.Cancelled, completed!.Kind);
        Assert.Equal(CancelReason.NoSelection, completed.Reason);
    }

    [Theory]
    [InlineData(CancelCause.Escape)]
    [InlineData(CancelCause.CtrlReleased)]
    [InlineData(CancelCause.ServiceStopped)]
    public void CancellingTheGesture_HidesTheOverlay_AndReportsTheCause(CancelCause cause)
    {
        var coordinator = Create();
        CancelCause? aborted = null;
        var completedCalls = 0;
        coordinator.Aborted += c => aborted = c;
        coordinator.Completed += _ => completedCalls++;
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        coordinator.Handle(Event(GestureEventKind.Cancelled, Center, 3_000, cause));

        Assert.False(coordinator.IsActive);
        Assert.False(_presenter.Visible);
        Assert.Equal(cause, aborted);
        Assert.Equal(0, completedCalls);
    }

    [Fact]
    public void ConfirmOrCancelWithoutAnActiveGesture_IsIgnored()
    {
        var coordinator = Create();
        var calls = 0;
        coordinator.Completed += _ => calls++;
        coordinator.Aborted += _ => calls++;

        coordinator.Handle(Event(GestureEventKind.Confirmed, Center, 2_000));
        coordinator.Handle(Event(GestureEventKind.Cancelled, Center, 2_001, CancelCause.Escape));

        Assert.Equal(0, calls);
        Assert.Equal(0, _presenter.Hides);
    }

    [Fact]
    public void AfterTheGestureEnds_PollingDoesNothing()
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        coordinator.Handle(Event(GestureEventKind.Confirmed, Center, 3_000));
        var selections = _presenter.Selections.Count;

        _pointer = new ScreenPoint(Center.X + 200, Center.Y);
        coordinator.Poll();

        Assert.Equal(selections, _presenter.Selections.Count);
    }

    [Fact]
    public void ANewActivation_ShowsTheOverlayAgain_WithAFreshSelection()
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));
        _pointer = new ScreenPoint(Center.X + 100, Center.Y);
        coordinator.Poll();
        coordinator.Handle(Event(GestureEventKind.Confirmed, _pointer, 3_000));

        _pointer = new ScreenPoint(300, 300);
        coordinator.Handle(Event(GestureEventKind.Activated, _pointer, 4_000));

        Assert.True(_presenter.Visible);
        Assert.Equal(2, _presenter.Shows);
        Assert.Equal(new ScreenPoint(300, 300), _presenter.LastCenter);
        Assert.True(coordinator.IsActive);
    }

    [Fact]
    public void IfTheOverlayFailsToShow_TheCoordinatorHidesItAndLeavesNothingActive()
    {
        _presenter.FailOnShow = new InvalidOperationException("sin ventana");
        var coordinator = Create();

        Assert.Throws<InvalidOperationException>(() => coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000)));

        Assert.False(coordinator.IsActive);
        Assert.False(_presenter.Visible);
    }

    // Aislamiento durante la autoprueba

    [Fact]
    public void WhileIsolated_NoEventShowsTheOverlay()
    {
        var coordinator = Create();
        coordinator.Isolate();

        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        Assert.True(coordinator.IsIsolated);
        Assert.False(coordinator.IsActive);
        Assert.Equal(0, _presenter.Shows);
    }

    [Fact]
    public void Isolating_HidesAnOverlayThatWasOnScreen()
    {
        var coordinator = Create();
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        coordinator.Isolate();

        Assert.False(coordinator.IsActive);
        Assert.False(_presenter.Visible);
    }

    [Fact]
    public void WhileIsolated_PollingAndEndOfGestureEventsDoNothing()
    {
        var coordinator = Create();
        coordinator.Isolate();
        var completed = 0;
        coordinator.Completed += _ => completed++;

        coordinator.Handle(Event(GestureEventKind.Confirmed, Center, 2_000));
        coordinator.Poll();

        Assert.Equal(0, completed);
        Assert.Equal(0, _presenter.Hides);
    }

    [Fact]
    public void AfterResuming_EventsLeftByTheSelfTestAreDiscarded_ByTheirTimestamp()
    {
        var coordinator = Create();
        coordinator.Isolate();
        _now = 10_000; // la autoprueba termina aquí
        coordinator.Resume();

        // Un evento de la autoprueba que aún estaba en el canal sin leer.
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 9_999));
        Assert.False(coordinator.IsActive);
        Assert.Equal(0, _presenter.Shows);

        // Un gesto real posterior sí se atiende.
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 10_001));
        Assert.True(coordinator.IsActive);
        Assert.Equal(1, _presenter.Shows);
    }

    [Fact]
    public void Dismiss_HidesTheOverlay_WithoutReportingAnything()
    {
        var coordinator = Create();
        var calls = 0;
        coordinator.Completed += _ => calls++;
        coordinator.Aborted += _ => calls++;
        coordinator.Handle(Event(GestureEventKind.Activated, Center, 2_000));

        coordinator.Dismiss();

        Assert.False(_presenter.Visible);
        Assert.False(coordinator.IsActive);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AnInvalidConfiguration_IsRejectedWhenTheCoordinatorIsCreated()
    {
        var invalid = RadialConfiguration.CreateEmpty() with { RootMenuId = new MenuId("no-existe") };

        Assert.Throws<InvalidConfigurationException>(() => new RadialOverlayCoordinator(invalid, new FakeClock(), _presenter, () => _pointer));
    }
}
