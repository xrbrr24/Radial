using Radial.Core.Engine;
using Radial.Core.Execution;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// Protección temporal contra la navegación entre menús: el motor sabe entrar en un submenú, pero el overlay solo representa el
/// menú raíz. Si el motor cambia de menú, el gesto se cancela sin ejecutar nada, para que nunca se vea un menú y se ejecute otro.
/// </summary>
public class RadialOverlayCoordinatorNavigationTests
{
    private sealed class FakeClock : IClock
    {
        public TimeSpan Now { get; set; }
    }

    private sealed class FakePresenter : IRadialPresenter
    {
        public bool Visible { get; private set; }

        public int Shows { get; private set; }

        public List<RadialMenu> ShownMenus { get; } = [];

        public List<RadialDirection?> Selections { get; } = [];

        public double Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
        {
            Visible = true;
            Shows++;
            ShownMenus.Add(menu);
            return 1.0;
        }

        public void SetSelection(RadialDirection? direction) => Selections.Add(direction);

        public void Hide() => Visible = false;
    }

    private sealed class FakeExecutor : IActionExecutor
    {
        public List<RadialAction> Executed { get; } = [];

        public bool CanExecute(RadialAction action) => true;

        public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
        {
            Executed.Add(action);
            return Task.FromResult(ActionExecutionResult.Success);
        }
    }

    private static readonly ScreenPoint Center = new(1000, 500);
    private static readonly MenuId RootId = new("main");
    private static readonly MenuId SubId = new("sub");
    private static readonly OpenFolderAction RootEastAction = new(@"C:\Temp\Root-East");
    private static readonly OpenFolderAction SubNorthAction = new(@"C:\Temp\Sub-North");
    private static readonly TimeSpan PastTheDwell = TimeSpan.FromMilliseconds(700); // la permanencia predeterminada es de 600 ms

    private readonly FakeClock _clock = new();
    private readonly FakePresenter _presenter = new();
    private readonly FakeExecutor _executor = new();
    private readonly RadialOverlayCoordinator _coordinator;
    private readonly List<RadialResult> _completed = [];
    private readonly List<CancelCause> _aborted = [];
    private readonly List<MenuId> _blocked = [];
    private ScreenPoint _pointer = Center;
    private long _sequence;
    private long _now = 1_000;

    public RadialOverlayCoordinatorNavigationTests()
    {
        _coordinator = new RadialOverlayCoordinator(TwoLevelConfiguration(), _clock, _presenter, () => _pointer, () => _now, _executor);
        _coordinator.Completed += _completed.Add;
        _coordinator.Aborted += _aborted.Add;
        _coordinator.UnsupportedNavigation += _blocked.Add;
    }

    /// <summary>Raíz: norte = grupo que abre «sub», este = acción. «sub»: norte = otra acción distinta.</summary>
    private static RadialConfiguration TwoLevelConfiguration() => new()
    {
        RootMenuId = RootId,
        Menus =
        [
            new RadialMenu(
                RootId,
                "Principal",
                [
                    new RadialItem(new ItemId("group"), "Grupo", RadialDirection.North, new OpenMenuAction(SubId)),
                    new RadialItem(new ItemId("root-east"), "Este", RadialDirection.East, RootEastAction),
                ]),
            new RadialMenu(SubId, "Sub", [new RadialItem(new ItemId("sub-north"), "Norte del submenú", RadialDirection.North, SubNorthAction)]),
        ],
        Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
    };

    private GestureEvent Event(GestureEventKind kind, ScreenPoint point, CancelCause cause = CancelCause.None) =>
        new(++_sequence, _now += 10, kind, point, cause);

    private static ScreenPoint North => new(Center.X, Center.Y - 120);

    private static ScreenPoint East => new(Center.X + 120, Center.Y);

    private void Activate() => _coordinator.Handle(Event(GestureEventKind.Activated, Center));

    private void Poll(ScreenPoint pointer)
    {
        _pointer = pointer;
        _coordinator.Poll();
    }

    /// <summary>Activa el gesto, se queda sobre el grupo y deja pasar la permanencia: el motor entra en «sub».</summary>
    private void HoverTheGroupUntilTheEngineEntersIt()
    {
        Activate();
        Poll(North);
        _clock.Now += PastTheDwell;
        Poll(North);
    }

    private void AssertNothingExecutedAndNothingVisible()
    {
        Assert.Empty(_executor.Executed);
        Assert.False(_presenter.Visible);
        Assert.False(_coordinator.IsActive);
        Assert.False(_coordinator.HasPendingAction);
    }

    // El motor cambia de menú durante el sondeo

    [Fact]
    public void WhenTheEngineEntersASubmenu_TheGestureIsCancelled_AndTheOverlayHidden()
    {
        HoverTheGroupUntilTheEngineEntersIt();

        AssertNothingExecutedAndNothingVisible();
        Assert.Equal(SubId, Assert.Single(_blocked));
        Assert.Empty(_completed);
        Assert.Empty(_aborted);
    }

    [Fact]
    public void BeforeTheDwellCompletes_NothingIsBlocked_AndTheGroupIsJustHighlighted()
    {
        Activate();
        Poll(North);
        _clock.Now += TimeSpan.FromMilliseconds(100);
        Poll(North);

        Assert.True(_coordinator.IsActive);
        Assert.True(_presenter.Visible);
        Assert.Empty(_blocked);
        Assert.Equal(RadialDirection.North, Assert.Single(_presenter.Selections));
    }

    [Fact]
    public void TheSelectionOfTheOtherMenu_IsNeverSentToThePresenter()
    {
        HoverTheGroupUntilTheEngineEntersIt();
        var selectionsAtTheBlock = _presenter.Selections.Count;

        Poll(East);
        Poll(North);

        // Solo se resaltó el grupo del menú raíz, que es lo que se veía; nada del submenú.
        Assert.Equal(selectionsAtTheBlock, _presenter.Selections.Count);
        Assert.Equal(RadialDirection.North, Assert.Single(_presenter.Selections));
        Assert.Equal(1, _presenter.Shows);
        Assert.Equal(RootId, Assert.Single(_presenter.ShownMenus).Id);
    }

    [Fact]
    public void ReleasingTheMiddleButtonAfterTheBlock_ExecutesNothing_AndConfirmsNothing()
    {
        HoverTheGroupUntilTheEngineEntersIt();

        // Sobre el mismo punto, el elemento norte del submenú sería la acción «Sub-North»: no puede ejecutarse.
        _coordinator.Handle(Event(GestureEventKind.Confirmed, North));

        AssertNothingExecutedAndNothingVisible();
        Assert.Empty(_completed);
    }

    [Fact]
    public void ReleasingCtrlAfterTheBlock_ReportsNothingMore()
    {
        HoverTheGroupUntilTheEngineEntersIt();

        _coordinator.Handle(Event(GestureEventKind.Cancelled, Center, CancelCause.CtrlReleased));

        AssertNothingExecutedAndNothingVisible();
        Assert.Empty(_aborted);
        Assert.Empty(_completed);
        Assert.Single(_blocked);
    }

    [Fact]
    public void PollingAfterTheBlock_DoesNothing()
    {
        HoverTheGroupUntilTheEngineEntersIt();

        _clock.Now += PastTheDwell;
        Poll(North);
        Poll(East);

        AssertNothingExecutedAndNothingVisible();
        Assert.Single(_blocked);
    }

    // El motor cambia de menú justo al confirmar

    [Fact]
    public void TheReleaseThatCompletesTheDwell_IsNotConfirmed()
    {
        Activate();
        Poll(North);
        _clock.Now += PastTheDwell; // la permanencia se completa sin que nadie sondee: la completa el propio evento de soltar

        _coordinator.Handle(Event(GestureEventKind.Confirmed, North));

        AssertNothingExecutedAndNothingVisible();
        Assert.Equal(SubId, Assert.Single(_blocked));
        Assert.Empty(_completed); // sin la protección se confirmaría «sin selección» en el menú nuevo
    }

    [Fact]
    public void ReleasingOverAGroupBeforeTheDwell_ExecutesNothing_AndIsNotUnsupportedNavigation()
    {
        Activate();
        Poll(North);
        _clock.Now += TimeSpan.FromMilliseconds(100);

        _coordinator.Handle(Event(GestureEventKind.Confirmed, North));

        AssertNothingExecutedAndNothingVisible();
        Assert.Empty(_blocked);
        var completed = Assert.Single(_completed);
        Assert.Equal(RadialResultKind.Cancelled, completed.Kind);
        Assert.Equal(CancelReason.GroupNotEntered, completed.Reason);
    }

    // Lo normal sigue funcionando

    [Fact]
    public void ConfirmingAnActionOfTheRootMenu_StillExecutesIt()
    {
        Activate();
        Poll(East);
        _coordinator.Handle(Event(GestureEventKind.Confirmed, East));

        Assert.Equal(RootEastAction, Assert.Single(_executor.Executed));
        Assert.Empty(_blocked);
        Assert.Equal(RadialResultKind.Execute, Assert.Single(_completed).Kind);
    }

    [Fact]
    public void AfterABlockedGesture_ANewGestureWorksNormally()
    {
        HoverTheGroupUntilTheEngineEntersIt();

        Activate();
        Assert.True(_coordinator.IsActive);
        Assert.True(_presenter.Visible);
        Assert.Equal(2, _presenter.Shows);
        Assert.Equal(RootId, _presenter.ShownMenus[^1].Id);

        Poll(East);
        _coordinator.Handle(Event(GestureEventKind.Confirmed, East));

        Assert.Equal(RootEastAction, Assert.Single(_executor.Executed));
        Assert.Single(_blocked);
    }

    [Fact]
    public void ASubscriberThatThrowsOnUnsupportedNavigation_IsReported_AndTheGestureIsStillCancelled()
    {
        var failures = new List<string>();
        _coordinator.SubscriberFailed += (name, _) => failures.Add(name);
        _coordinator.UnsupportedNavigation += _ => throw new InvalidOperationException("suscriptor defectuoso");

        HoverTheGroupUntilTheEngineEntersIt();

        AssertNothingExecutedAndNothingVisible();
        Assert.Equal(nameof(RadialOverlayCoordinator.UnsupportedNavigation), Assert.Single(failures));
    }
}
