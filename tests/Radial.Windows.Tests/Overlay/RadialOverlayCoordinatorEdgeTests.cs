using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// Radial junto a un borde de pantalla. El presentador real desplaza la ventana hacia dentro para que quepa en el monitor y devuelve
/// el centro dibujado; el coordinador tiene que medir la selección desde ESE centro (lo que se ve es lo que se selecciona) y no
/// seleccionar nada hasta que el cursor se mueva, porque el cursor queda lejos del centro, a menudo sobre un sector.
/// El ajuste lo imita <see cref="RecordingPresenter.FitCenter"/>; el cálculo real está en <c>OverlayPlacementTests</c>.
/// </summary>
public class RadialOverlayCoordinatorEdgeTests
{
    private static readonly MenuId MainId = new("main");
    private static readonly MenuId AId = new("a");
    private static readonly OpenFolderAction West = new(@"C:\Temp\West");
    private static readonly OpenFolderAction East = new(@"C:\Temp\East");
    private static readonly OpenFolderAction ASouth = new(@"C:\Temp\A-South");

    /// <summary>Distancia a la que el radial se desplaza hacia dentro en estas pruebas.</summary>
    private const int Shift = 200;

    private static RadialConfiguration Config() => new()
    {
        RootMenuId = MainId,
        Menus =
        [
            new RadialMenu(
                MainId,
                "Principal",
                [
                    new RadialItem(new ItemId("main.west"), "Oeste", RadialDirection.West, West),
                    new RadialItem(new ItemId("main.east"), "Este", RadialDirection.East, East),
                    new RadialItem(new ItemId("main.a"), "A", RadialDirection.North, new OpenMenuAction(AId)),
                ]),
            new RadialMenu(
                AId,
                "Menú A",
                [
                    new RadialItem(new ItemId("a.south"), "Sur", RadialDirection.South, ASouth),
                    new RadialItem(new ItemId("a.west"), "Oeste", RadialDirection.West, new OpenFolderAction(@"C:\Temp\A-West")),
                ]),
        ],
        Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
    };

    private static CoordinatorHarness AtLeftEdge(double scale = 1.0)
    {
        var presenter = new RecordingPresenter
        {
            Scale = scale,
            FitCenter = requested => new ScreenPoint(requested.X + Shift, requested.Y),
        };
        return new CoordinatorHarness(Config(), presenter);
    }

    private static void ActivateAt(CoordinatorHarness h, ScreenPoint cursor)
    {
        h.Move(cursor);
        h.Activate(cursor);
    }

    private static bool AnythingSelected(CoordinatorHarness h) => h.Presenter.Selections.Any(s => s is not null);

    [Fact]
    public void ReleasingWithoutMoving_AtAnEdge_ExecutesNothing_EvenThoughTheCursorIsOverASector()
    {
        var h = AtLeftEdge();
        var cursor = new ScreenPoint(0, 500); // 200 px al oeste del centro dibujado: justo sobre el sector del oeste

        ActivateAt(h, cursor);
        h.Release(cursor);

        var result = Assert.Single(h.Completed);
        Assert.Equal(RadialResultKind.Cancelled, result.Kind);
        Assert.Equal(CancelReason.NoSelection, result.Reason);
        Assert.Empty(h.Executor.Executed);
        Assert.False(AnythingSelected(h));
    }

    [Fact]
    public void AfterTheCursorMoves_TheSelectionIsMeasuredFromTheDrawnCenter_NotFromTheCursorAtActivation()
    {
        var h = AtLeftEdge();
        ActivateAt(h, new ScreenPoint(0, 500)); // centro dibujado: (200, 500)

        // Un paso a la DERECHA del cursor: desde el cursor sería «este», pero el centro dibujado está más a la derecha, así que sigue
        // siendo el sector del oeste. Es lo que el usuario ve.
        h.Move(new ScreenPoint(15, 500));

        Assert.Equal(RadialDirection.West, h.Presenter.Selections[^1]);
    }

    [Fact]
    public async Task ConfirmingAtAnEdge_ExecutesWhatIsDrawnUnderTheCursor()
    {
        var h = AtLeftEdge();
        ActivateAt(h, new ScreenPoint(0, 500));

        h.Move(new ScreenPoint(15, 500));
        h.Release(new ScreenPoint(15, 500));
        var (item, result) = await h.NextFinishedAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(West, item.Action);
        Assert.Equal(West, Assert.Single(h.Executor.Executed));
    }

    [Fact]
    public void TheEastSectorIsReachableFromAnEdgeToo()
    {
        var h = AtLeftEdge();
        ActivateAt(h, new ScreenPoint(0, 500));

        h.Move(new ScreenPoint(300, 500)); // 100 px al este del centro dibujado (200, 500)

        Assert.Equal(RadialDirection.East, h.Presenter.Selections[^1]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(40, true)]
    public void SelectionWakesUpAfterTenDips(int movedPixels, bool selects)
    {
        var h = AtLeftEdge();
        ActivateAt(h, new ScreenPoint(0, 500));

        h.Move(new ScreenPoint(movedPixels, 500));

        Assert.Equal(selects, AnythingSelected(h));
    }

    [Theory]
    [InlineData(2.0, 19, false)] // al 200 %, 10 DIP son 20 píxeles
    [InlineData(2.0, 20, true)]
    [InlineData(1.5, 14, false)] // al 150 %, 15 píxeles
    [InlineData(1.5, 15, true)]
    [InlineData(1.0, 9, false)]
    [InlineData(1.0, 10, true)]
    public void TheWakeUpDistanceIsInDips_SoItScalesWithTheMonitor(double scale, int movedPixels, bool selects)
    {
        var h = AtLeftEdge(scale);
        ActivateAt(h, new ScreenPoint(0, 500));

        h.Move(new ScreenPoint(movedPixels, 500));

        Assert.Equal(selects, AnythingSelected(h));
    }

    [Fact]
    public void WithoutAnEdge_NothingChanges_TheSelectionStartsImmediately()
    {
        var h = new CoordinatorHarness(Config()); // sin ajuste: el centro dibujado es el cursor
        ActivateAt(h, CoordinatorHarness.Start);

        h.Move(new ScreenPoint(CoordinatorHarness.Start.X + 120, CoordinatorHarness.Start.Y));

        Assert.Equal(RadialDirection.East, h.Presenter.Selections[^1]);
    }

    [Fact]
    public void ASmallJitterBeforeTheWakeUp_DoesNotSelect_AndNoGroupIsEnteredByWaiting()
    {
        var h = AtLeftEdge();
        ActivateAt(h, new ScreenPoint(0, 500));

        h.Move(new ScreenPoint(3, 501));
        h.Dwell(5_000);

        Assert.Empty(h.MenuChanges);
        Assert.True(h.Coordinator.IsActive);
    }

    [Fact]
    public void ASubmenuDrawnAfterAShift_IsMeasuredFromItsDrawnCenter_AndWaitsForTheCursorToMove()
    {
        // El centro raíz está libre; el ajuste solo se aplica al submenú.
        var h = new CoordinatorHarness(Config());
        h.Move(CoordinatorHarness.Start);
        h.Activate();
        var group = new ScreenPoint(CoordinatorHarness.Start.X, CoordinatorHarness.Start.Y - 120);
        h.Move(group);
        h.Presenter.FitCenter = requested => new ScreenPoint(requested.X + Shift, requested.Y);
        h.Dwell(700);

        Assert.Equal(AId, h.Presenter.Menus[^1].Menu.Id);
        var selectionsBefore = h.Presenter.Selections.Count(s => s is not null);

        // Sin moverse, el cursor está 200 px al oeste del centro dibujado del submenú: no se selecciona nada, pase lo que pase.
        h.Dwell(2_000);
        Assert.Equal(selectionsBefore, h.Presenter.Selections.Count(s => s is not null));

        // Al moverse, manda el centro dibujado: un paso a la derecha sigue siendo «oeste» del submenú.
        h.Move(new ScreenPoint(group.X + 12, group.Y));
        Assert.Equal(RadialDirection.West, h.Presenter.Selections[^1]);
    }

    [Fact]
    public async Task ANewGestureAwayFromTheEdges_StartsArmed_AfterOneAtAnEdge()
    {
        var h = AtLeftEdge();
        var edge = new ScreenPoint(0, 500);
        ActivateAt(h, edge);
        h.Release(edge);

        h.Presenter.FitCenter = null; // ahora, en mitad de la pantalla
        h.Move(CoordinatorHarness.Start);
        h.Activate();
        var east = new ScreenPoint(CoordinatorHarness.Start.X + 120, CoordinatorHarness.Start.Y);
        h.Move(east);
        h.Release(east);
        var (item, _) = await h.NextFinishedAsync();

        Assert.Equal(East, item.Action);
    }
}
