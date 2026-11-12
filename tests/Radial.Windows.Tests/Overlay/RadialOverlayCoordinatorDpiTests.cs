using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// El radial se recoloca SOLO con el overlay visible (cambio de DPI al pasar a otro monitor, <c>WM_DPICHANGED</c>): el presentador lo
/// notifica con <see cref="IRadialPresenter.PlacementChanged"/> y el coordinador tiene que seguir midiendo la selección desde el centro
/// dibujado y con la escala del monitor donde está ahora. Aquí se prueba esa lógica con un presentador falso; el evento real de Windows no
/// se puede provocar en una prueba automática (hace falta mover la ventana entre monitores con distinto DPI: ver la validación manual).
/// </summary>
public class RadialOverlayCoordinatorDpiTests
{
    private static readonly MenuId MainId = new("main");
    private static readonly OpenFolderAction East = new(@"C:\Temp\East");
    private static readonly OpenFolderAction West = new(@"C:\Temp\West");

    private static RadialConfiguration Config() => new()
    {
        RootMenuId = MainId,
        Menus =
        [
            new RadialMenu(
                MainId,
                "Principal",
                [
                    new RadialItem(new ItemId("main.east"), "Este", RadialDirection.East, East),
                    new RadialItem(new ItemId("main.west"), "Oeste", RadialDirection.West, West),
                ]),
        ],
        Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
    };

    private static ScreenPoint Right(ScreenPoint from, int pixels) => new(from.X + pixels, from.Y);

    private static CoordinatorHarness Active()
    {
        var h = new CoordinatorHarness(Config());
        h.Move(CoordinatorHarness.Start);
        h.Activate();
        return h;
    }

    private static bool AnythingSelected(CoordinatorHarness h) => h.Presenter.Selections.Any(s => s is not null);

    [Fact]
    public void ADpiChange_MakesEveryLaterMeasurementUseTheNewScale()
    {
        var h = Active();
        var cursor = Right(CoordinatorHarness.Start, 60); // 60 px = 60 DIP a escala 1: fuera de la zona central (40 DIP)
        h.Move(cursor);
        Assert.Equal(RadialDirection.East, h.Presenter.Selections[^1]);

        // El mismo cursor, al 200 %, son 30 DIP: dentro de la zona central.
        h.Presenter.RaisePlacementChanged(new PresentedPlacement(2.0, CoordinatorHarness.Start));
        h.Move(cursor);

        Assert.Null(h.Presenter.Selections[^1]);
    }

    [Fact]
    public void ADpiChange_ThatMovesTheCenter_MakesTheSelectionFollowTheNewCenter()
    {
        var h = Active();
        var cursor = Right(CoordinatorHarness.Start, 120);
        h.Move(cursor);
        Assert.Equal(RadialDirection.East, h.Presenter.Selections[^1]);

        // La ventana se recolocó 200 px a la derecha: el cursor, que no se ha movido, queda ahora a su izquierda.
        h.Presenter.RaisePlacementChanged(new PresentedPlacement(1.0, Right(CoordinatorHarness.Start, 200)));
        h.Move(cursor);

        Assert.Equal(RadialDirection.West, h.Presenter.Selections[^1]);
    }

    [Fact]
    public async Task ADpiChange_ThatDisplacesTheCenter_BeforeTheCursorMoves_WaitsForTheCursor_AndConfirmingExecutesNothing()
    {
        var h = Active();

        // Sin mover el cursor, el centro dibujado salta 200 px: el cursor quedaría sobre el sector del oeste sin haber hecho nada.
        h.Presenter.RaisePlacementChanged(new PresentedPlacement(1.0, Right(CoordinatorHarness.Start, 200)));
        h.Move(CoordinatorHarness.Start);
        Assert.False(AnythingSelected(h));

        h.Release(CoordinatorHarness.Start);
        var result = Assert.Single(h.Completed);
        Assert.Equal(CancelReason.NoSelection, result.Reason);
        Assert.Empty(h.Executor.Executed);

        // Y con un gesto nuevo, tras mover el cursor, sí selecciona desde el centro nuevo.
        h.Move(CoordinatorHarness.Start);
        h.Activate();
        h.Presenter.RaisePlacementChanged(new PresentedPlacement(1.0, Right(CoordinatorHarness.Start, 200)));
        h.Move(Right(CoordinatorHarness.Start, 12));
        Assert.Equal(RadialDirection.West, h.Presenter.Selections[^1]);
        h.Release(Right(CoordinatorHarness.Start, 12));
        var (item, _) = await h.NextFinishedAsync();
        Assert.Equal(West, item.Action);
    }

    [Fact]
    public void ADpiChange_ThatLeavesTheCenterWhereItWas_ChangesNothingExceptTheScale()
    {
        var h = Active();
        var cursor = Right(CoordinatorHarness.Start, 120);

        h.Presenter.RaisePlacementChanged(new PresentedPlacement(1.0, CoordinatorHarness.Start));
        h.Move(cursor);

        Assert.Equal(RadialDirection.East, h.Presenter.Selections[^1]);
    }

    [Fact]
    public void TheCoordinatorOnlyListensWhileARadialIsOnScreen()
    {
        var h = new CoordinatorHarness(Config());
        Assert.False(h.Presenter.HasPlacementSubscribers);

        // Con el radial oculto, la notificación se ignora (y no hay nadie suscrito).
        h.Presenter.RaisePlacementChanged(new PresentedPlacement(2.0, new ScreenPoint(5, 5)));
        Assert.Empty(h.Presenter.Selections);

        h.Move(CoordinatorHarness.Start);
        h.Activate();
        Assert.True(h.Presenter.HasPlacementSubscribers);

        h.Release(CoordinatorHarness.Start);
        Assert.False(h.Presenter.HasPlacementSubscribers); // confirmar

        h.Move(CoordinatorHarness.Start);
        h.Activate();
        Assert.True(h.Presenter.HasPlacementSubscribers);
        h.Cancel(CancelCause.Escape);
        Assert.False(h.Presenter.HasPlacementSubscribers); // cancelar

        h.Move(CoordinatorHarness.Start);
        h.Activate();
        h.Coordinator.Dismiss();
        Assert.False(h.Presenter.HasPlacementSubscribers); // cerrar la aplicación
    }
}
