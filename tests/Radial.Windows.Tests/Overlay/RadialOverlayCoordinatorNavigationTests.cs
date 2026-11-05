using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>
/// Navegación entre menús en el overlay (fase 5): entrar en un grupo por permanencia, recentrar sin ocultar la ventana, progreso,
/// retroceso armado, tres niveles, y que el menú mostrado y la acción ejecutada coincidan siempre. Coordinador con presentador,
/// ejecutor y reloj falsos: el tiempo lo avanzan las pruebas, nunca hay esperas reales. Cursor en píxeles con escala 1.
/// </summary>
public class RadialOverlayCoordinatorNavigationTests
{
    private static readonly MenuId MainId = new("main");
    private static readonly MenuId AId = new("a");
    private static readonly MenuId BId = new("b");

    private static readonly OpenFolderAction RootEast = new(@"C:\Temp\Root-East");
    private static readonly OpenFolderAction ASouth = new(@"C:\Temp\A-South");
    private static readonly OpenFolderAction BNorth = new(@"C:\Temp\B-North");

    // Dwell predeterminados: 600 ms para entrar y 800 ms para volver.
    private const double EnterDwell = 600;
    private const double BackDwell = 800;

    /// <summary>
    /// Tres niveles: <c>main</c> (norte = grupo «a», este = acción) → <c>a</c> (norte = grupo «b», sur = acción) → <c>b</c>
    /// (norte = acción, este = acción desconocida). Zona central de 40 DIP.
    /// </summary>
    private static RadialConfiguration ThreeLevels() => new()
    {
        RootMenuId = MainId,
        Menus =
        [
            new RadialMenu(
                MainId,
                "Principal",
                [
                    new RadialItem(new ItemId("main.a"), "A", RadialDirection.North, new OpenMenuAction(AId)),
                    new RadialItem(new ItemId("main.east"), "Este", RadialDirection.East, RootEast),
                ]),
            new RadialMenu(
                AId,
                "Menú A",
                [
                    new RadialItem(new ItemId("a.b"), "B", RadialDirection.North, new OpenMenuAction(BId)),
                    new RadialItem(new ItemId("a.south"), "Sur", RadialDirection.South, ASouth),
                ]),
            new RadialMenu(
                BId,
                "Menú B",
                [
                    new RadialItem(new ItemId("b.north"), "Norte", RadialDirection.North, BNorth),
                    new RadialItem(new ItemId("b.future"), "Futura", RadialDirection.East, new UnknownAction("futureAction", "{\"type\":\"futureAction\"}")),
                ]),
        ],
        Geometry = new GeometrySettings { DeadZoneRadius = 40, HysteresisDegrees = 0 },
    };

    private static ScreenPoint Offset(ScreenPoint center, int dx, int dy) => new(center.X + dx, center.Y + dy);

    private static ScreenPoint Up(ScreenPoint center, int distance = 120) => Offset(center, 0, -distance);

    private static ScreenPoint Down(ScreenPoint center, int distance = 120) => Offset(center, 0, distance);

    private static ScreenPoint Right(ScreenPoint center, int distance = 120) => Offset(center, distance, 0);

    /// <summary>Activa y entra en «a»: el radial queda recentrado donde estaba el cursor al completarse la permanencia.</summary>
    private static ScreenPoint EnterA(CoordinatorHarness h)
    {
        h.Activate();
        var group = Up(CoordinatorHarness.Start);
        h.Move(group);
        h.Dwell(EnterDwell + 100);
        return group; // el centro nuevo es el cursor
    }

    // Entrar en un grupo

    [Fact]
    public void DwellingOnAGroupForTheConfiguredTime_EntersItsSubmenu()
    {
        var h = new CoordinatorHarness(ThreeLevels());

        var center = EnterA(h);

        Assert.True(h.Coordinator.IsActive);
        Assert.Equal(AId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(2, h.Presenter.Menus[^1].Depth);
        Assert.Equal(center, h.Presenter.Menus[^1].Center);
        Assert.Equal((AId, 2), Assert.Single(h.MenuChanges));
        Assert.Empty(h.Blocked);
        Assert.Empty(h.Completed);
    }

    [Fact]
    public void BeforeTheDwellCompletes_TheMenuDoesNotChange_AndTheGroupJustHighlights()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        h.Activate();

        h.Move(Up(CoordinatorHarness.Start));
        h.Dwell(EnterDwell - 100);

        Assert.Equal(0, h.Presenter.MenuChanges);
        Assert.Equal(MainId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(RadialDirection.North, Assert.Single(h.Presenter.Selections));
        Assert.Empty(h.MenuChanges);
    }

    [Fact]
    public void TheEntryProgress_GrowsGraduallyWhileDwelling()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        h.Activate();
        h.Move(Up(CoordinatorHarness.Start));

        h.Dwell(150);
        h.Dwell(150);
        h.Dwell(150);

        // El primer valor (0) es el de llegar al grupo; después crece con el tiempo transcurrido.
        var values = h.Presenter.Progresses.Select(p => Math.Round(p.Submenu, 3)).ToArray();
        Assert.Equal(new[] { 0.0, 0.25, 0.5, 0.75 }, values);
    }

    [Fact]
    public void LeavingTheGroupBeforeTheDwellCompletes_ResetsTheProgress_AndDoesNotEnter()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        h.Activate();
        h.Move(Up(CoordinatorHarness.Start));
        h.Dwell(300);
        Assert.Equal(0.5, h.Presenter.Progresses[^1].Submenu, 3);

        h.Move(Right(CoordinatorHarness.Start)); // sobre una acción: el contador del grupo se anula

        Assert.Equal(0.0, h.Presenter.Progresses[^1].Submenu);
        h.Dwell(EnterDwell + 100);
        Assert.Equal(0, h.Presenter.MenuChanges);
    }

    [Fact]
    public void EnteringASubmenu_ChangesTheMenuInPlace_WithoutHidingOrReshowingTheWindow()
    {
        var h = new CoordinatorHarness(ThreeLevels());

        EnterA(h);

        Assert.Equal(1, h.Presenter.Shows);
        Assert.Equal(0, h.Presenter.Hides);
        Assert.True(h.Presenter.Visible);

        // «show» una sola vez y «menu:» entre medias, sin ningún «hide».
        var shows = h.Presenter.Log.Where(entry => entry == "show").ToList();
        Assert.Single(shows);
        Assert.DoesNotContain("hide", h.Presenter.Log);
        Assert.Contains("menu:Menú A", h.Presenter.Log);
    }

    [Fact]
    public void AfterEntering_TheSelectionIsEmpty_UntilTheCursorLeavesTheNewCenter()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);
        var selectionsAtEntry = h.Presenter.Selections.Count;

        h.Move(center); // en el centro nuevo: nada seleccionado, y no se notifica un cambio inexistente
        Assert.Equal(selectionsAtEntry, h.Presenter.Selections.Count);
        Assert.Equal(0.0, h.Presenter.Progresses[^1].Submenu);

        h.Move(Down(center)); // el sector sur del submenú
        Assert.Equal(RadialDirection.South, h.Presenter.Selections[^1]);
        Assert.Equal(selectionsAtEntry + 1, h.Presenter.Selections.Count);
    }

    // Recentrado y coordenadas

    [Fact]
    public void AfterRecentring_TheCursorIsMeasuredAgainstTheNewCenter_NotTheOldOne()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        EnterA(h);

        // El punto donde se activó el gesto está ahora 120 px POR DEBAJO del centro nuevo: es el sector sur del submenú. Medido contra
        // el origen antiguo caería en la zona central y no seleccionaría nada.
        h.Release(CoordinatorHarness.Start);

        Assert.Equal(ASouth, Assert.Single(h.Executor.Executed));
        Assert.Equal(RadialResultKind.Execute, Assert.Single(h.Completed).Kind);
    }

    [Fact]
    public void WhenTheNewMenuIsOnAnotherMonitorScale_TheCoordinatesAreRebasedWithoutAJump()
    {
        // Primer menú al 125 % (escala 1,25) y el siguiente en un monitor al 200 %.
        var presenter = new RecordingPresenter { Scale = 1.25, MenuScale = 2.0 };
        var h = new CoordinatorHarness(ThreeLevels(), presenter);
        h.Activate();
        var group = Offset(CoordinatorHarness.Start, 0, -150); // 150 px / 1,25 = 120 DIP: sector norte
        h.Move(group);
        h.Dwell(EnterDwell + 100);
        Assert.Equal(1, presenter.MenuChanges);
        var selectionsAtEntry = presenter.Selections.Count;

        // 60 px con la escala nueva (2,0) son 30 DIP: dentro de la zona central (40). Con la escala vieja serían 48 DIP y
        // seleccionaría el sector sur: la conversión se desincronizaría.
        h.Move(Down(group, 60));
        Assert.Equal(selectionsAtEntry, presenter.Selections.Count);

        // 240 px / 2,0 = 120 DIP: sector sur del submenú.
        h.Move(Down(group, 240));
        Assert.Equal(RadialDirection.South, presenter.Selections[^1]);
    }

    // Retroceso

    [Fact]
    public void GoingBack_IsNotAllowed_BeforeTheCursorHasLeftTheCenter()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);

        var progressAtEntry = h.Presenter.Progresses.Count;

        h.Move(center);
        h.Dwell(BackDwell * 3); // el cursor no salió del centro nuevo: el retroceso no está armado

        Assert.Equal(1, h.Presenter.MenuChanges);
        Assert.Equal(AId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(progressAtEntry, h.Presenter.Progresses.Count); // ni armado ni progreso: no se publicó nada nuevo
    }

    [Fact]
    public void LeavingTheCenter_ArmsTheBackNavigation()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);

        h.Move(Down(center, 80)); // fuera de la zona central, sobre una acción (el sector sur)

        Assert.True(h.Presenter.Progresses[^1].BackArmed);
        Assert.Equal(0.0, h.Presenter.Progresses[^1].Back);
    }

    [Fact]
    public void ReturningToTheCenterAfterArming_GoesBackAfterTheConfiguredTime_AndRecentresOnTheCursor()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);
        h.Move(Down(center, 80));

        h.Move(center);
        h.Dwell(400);
        Assert.Equal(0.5, h.Presenter.Progresses[^1].Back, 3);
        Assert.Equal(1, h.Presenter.MenuChanges); // todavía en «a»

        h.Dwell(BackDwell - 400 + 50);

        Assert.Equal(2, h.Presenter.MenuChanges);
        Assert.Equal(MainId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(1, h.Presenter.Menus[^1].Depth);
        Assert.Equal(center, h.Presenter.Menus[^1].Center);
        Assert.Equal(new[] { (AId, 2), (MainId, 1) }, h.MenuChanges.ToArray());
        Assert.Equal(0, h.Presenter.Hides);
    }

    [Fact]
    public void GoingBack_DoesNotCompleteBeforeTheBackDwell()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);
        h.Move(Down(center, 80));
        h.Move(center);

        h.Dwell(BackDwell - 100);

        Assert.Equal(1, h.Presenter.MenuChanges);
        Assert.Equal(AId, h.Presenter.Menus[^1].Menu.Id);
    }

    [Fact]
    public void LeavingTheCenterAgainWhileGoingBack_CancelsThatCountdown()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);
        h.Move(Down(center, 80));
        h.Move(center);
        h.Dwell(400);

        h.Move(Down(center, 80));

        Assert.Equal(0.0, h.Presenter.Progresses[^1].Back);
        h.Dwell(BackDwell * 2);
        Assert.Equal(1, h.Presenter.MenuChanges);
    }

    // Tres niveles

    [Fact]
    public void ThreeLevels_CanBeNavigatedDownAndBackUpOneStepAtATime()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var centerA = EnterA(h);

        // a -> b: el grupo norte de «a», 120 px por encima del centro de «a».
        var groupB = Up(centerA);
        h.Move(groupB);
        h.Dwell(EnterDwell + 100);
        Assert.Equal(BId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(3, h.Presenter.Menus[^1].Depth);
        Assert.Equal(groupB, h.Presenter.Menus[^1].Center);

        // b -> a: salir del centro de «b» (sobre una acción) y volver.
        h.Move(Up(groupB, 80));
        h.Move(groupB);
        h.Dwell(BackDwell + 50);
        Assert.Equal(AId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(2, h.Presenter.Menus[^1].Depth);
        Assert.Equal(groupB, h.Presenter.Menus[^1].Center);

        // a -> main.
        h.Move(Down(groupB, 80));
        h.Move(groupB);
        h.Dwell(BackDwell + 50);
        Assert.Equal(MainId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(1, h.Presenter.Menus[^1].Depth);

        Assert.Equal(new[] { (AId, 2), (BId, 3), (AId, 2), (MainId, 1) }, h.MenuChanges.ToArray());
        Assert.Equal(1, h.Presenter.Shows);
        Assert.Equal(0, h.Presenter.Hides);
        Assert.Empty(h.Blocked);
    }

    // Confirmar y cancelar

    [Fact]
    public void ConfirmingInAThirdLevelMenu_ExecutesThatMenusAction()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var centerA = EnterA(h);
        var centerB = Up(centerA);
        h.Move(centerB);
        h.Dwell(EnterDwell + 100);

        h.Release(Up(centerB)); // el sector norte de «b»

        Assert.Equal(BNorth, Assert.Single(h.Executor.Executed));
        Assert.False(h.Presenter.Visible);
        Assert.False(h.Coordinator.IsActive);
    }

    [Fact]
    public void ConfirmingInASubmenu_ExecutesTheSubmenuItem_NotTheRootOne()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);

        h.Release(Down(center));

        Assert.Equal(ASouth, Assert.Single(h.Executor.Executed));
        Assert.DoesNotContain(RootEast, h.Executor.Executed);
        Assert.Equal(RadialResultKind.Execute, Assert.Single(h.Completed).Kind);
        Assert.False(h.Presenter.Visible);
        Assert.Equal(1, h.Presenter.Hides);
    }

    [Theory]
    [InlineData(CancelCause.Escape)]
    [InlineData(CancelCause.CtrlReleased)]
    public void CancellingInASubmenu_HidesTheOverlay_AndExecutesNothing(CancelCause cause)
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);
        h.Move(Down(center));

        h.Cancel(cause);

        Assert.Empty(h.Executor.Executed);
        Assert.False(h.Presenter.Visible);
        Assert.False(h.Coordinator.IsActive);
        Assert.Equal(cause, Assert.Single(h.Aborted));
        Assert.Empty(h.Completed);
    }

    [Fact]
    public void ReleasingAtTheExactMomentTheDwellCompletes_ConfirmsNothingFromTheNewMenu()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        h.Activate();
        var group = Up(CoordinatorHarness.Start);
        h.Move(group);
        h.Wait(EnterDwell + 100); // la permanencia se completa sin que nadie sondee: la completa el propio evento de soltar

        h.Release(group);

        Assert.Empty(h.Executor.Executed);
        var completed = Assert.Single(h.Completed);
        Assert.Equal(RadialResultKind.Cancelled, completed.Kind);
        Assert.Equal(CancelReason.NoSelection, completed.Reason);
        Assert.False(h.Presenter.Visible);
    }

    [Fact]
    public void ReleasingOverAGroupBeforeTheDwell_ExecutesNothing()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        h.Activate();
        var group = Up(CoordinatorHarness.Start);
        h.Move(group);
        h.Dwell(100);

        h.Release(group);

        Assert.Empty(h.Executor.Executed);
        Assert.Equal(CancelReason.GroupNotEntered, Assert.Single(h.Completed).Reason);
        Assert.Equal(0, h.Presenter.MenuChanges);
    }

    [Fact]
    public void ReleasingOverAnUnsupportedAction_ExecutesNothing()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var centerA = EnterA(h);
        var centerB = Up(centerA);
        h.Move(centerB);
        h.Dwell(EnterDwell + 100);

        h.Release(Right(centerB)); // «Futura»: tipo de acción desconocido

        Assert.Empty(h.Executor.Executed);
        Assert.Equal(CancelReason.UnsupportedAction, Assert.Single(h.Completed).Reason);
    }

    // Nunca hay discrepancia entre lo mostrado y lo ejecutado

    [Fact]
    public void TheExecutedAction_AlwaysBelongsToTheMenuThatWasShown()
    {
        // Para cada menú (raíz, «a» y «b») y cada dirección con una acción: se confirma y se comprueba que la acción ejecutada es
        // un elemento del ÚLTIMO menú que se mostró.
        (Func<CoordinatorHarness, ScreenPoint> Reach, RadialDirection Direction)[] cases =
        [
            (h => { h.Activate(); return CoordinatorHarness.Start; }, RadialDirection.East),
            (h => EnterA(h), RadialDirection.South),
            (h =>
            {
                var a = EnterA(h);
                var b = Up(a);
                h.Move(b);
                h.Dwell(EnterDwell + 100);
                return b;
            }, RadialDirection.North),
        ];

        foreach (var (reach, direction) in cases)
        {
            var h = new CoordinatorHarness(ThreeLevels());
            var center = reach(h);

            h.Release(direction switch
            {
                RadialDirection.North => Up(center),
                RadialDirection.South => Down(center),
                _ => Right(center),
            });

            var shown = h.Presenter.Menus[^1].Menu;
            var executed = Assert.Single(h.Executor.Executed);
            Assert.Contains(shown.Items, item => ReferenceEquals(item.Action, executed));
        }
    }

    // Gestos nuevos, aislamiento y fallos

    [Fact]
    public void ANewGesture_AfterNavigating_StartsAgainAtTheRootMenu()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        EnterA(h);
        h.Cancel(CancelCause.Escape);
        var changesBefore = h.Presenter.MenuChanges;

        h.Activate();

        Assert.Equal(2, h.Presenter.Shows);
        Assert.Equal(MainId, h.Presenter.Menus[^1].Menu.Id);
        Assert.Equal(1, h.Presenter.Menus[^1].Depth);
        Assert.Equal(changesBefore, h.Presenter.MenuChanges);

        // Y vuelve a funcionar la entrada en el grupo desde cero.
        h.Move(Up(CoordinatorHarness.Start));
        h.Dwell(EnterDwell + 100);
        Assert.Equal(AId, h.Presenter.Menus[^1].Menu.Id);
    }

    [Fact]
    public void IsolatingWhileInASubmenu_HidesTheOverlay_AndIgnoresEverythingAfterwards()
    {
        var h = new CoordinatorHarness(ThreeLevels());
        var center = EnterA(h);

        h.Coordinator.Isolate();
        h.Move(Down(center));
        h.Release(Down(center));

        Assert.False(h.Presenter.Visible);
        Assert.False(h.Coordinator.IsActive);
        Assert.Empty(h.Executor.Executed);
        Assert.Empty(h.Completed);
    }

    [Fact]
    public void AFailureChangingTheMenu_CancelsTheGestureSafely()
    {
        var presenter = new RecordingPresenter { FailOnShowMenu = new InvalidOperationException("la ventana falló") };
        var h = new CoordinatorHarness(ThreeLevels(), presenter);
        h.Activate();
        h.Move(Up(CoordinatorHarness.Start));

        Assert.Throws<InvalidOperationException>(() => h.Dwell(EnterDwell + 100));

        Assert.False(h.Coordinator.IsActive);
        Assert.False(presenter.Visible);
        Assert.Empty(h.Executor.Executed);

        // Un evento de soltar posterior no ejecuta nada.
        h.Release(Down(CoordinatorHarness.Start));
        Assert.Empty(h.Executor.Executed);
    }

    // Protección contra pilas de menús incoherentes (la red de seguridad que evita mostrar un menú y ejecutar otro)

    private static RadialMenu Menu(string id) => new(new MenuId(id), id, []);

    [Fact]
    public void Classify_RecognisesTheValidTransitions()
    {
        var root = Menu("root");
        var child = Menu("child");
        var grandchild = Menu("grandchild");

        Assert.Equal(RadialOverlayCoordinator.NavigationStep.None, RadialOverlayCoordinator.Classify([root], [root]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.None, RadialOverlayCoordinator.Classify([root, child], [root, child]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Enter, RadialOverlayCoordinator.Classify([root], [root, child]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Enter, RadialOverlayCoordinator.Classify([root, child], [root, child, grandchild]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Back, RadialOverlayCoordinator.Classify([root, child], [root]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Back, RadialOverlayCoordinator.Classify([root, child, grandchild], [root, child]));
    }

    [Fact]
    public void Classify_RejectsEveryAnomalousState()
    {
        var root = Menu("root");
        var child = Menu("child");
        var grandchild = Menu("grandchild");
        var other = Menu("other");
        var equalButDifferentInstance = Menu("root");

        // Pilas vacías.
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root], []));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([], [root]));

        // Un salto de varios niveles, hacia dentro o hacia fuera.
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root], [root, child, grandchild]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root, child, grandchild], [root]));

        // Mismo número de niveles pero otro menú.
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root], [other]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root, child], [root, other]));

        // Un nivel más o menos, pero sin que una pila sea prefijo de la otra.
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root], [other, child]));
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root, child], [child]));

        // Mismo contenido pero otra instancia: no es «el mismo menú» que se está mostrando.
        Assert.Equal(RadialOverlayCoordinator.NavigationStep.Inconsistent, RadialOverlayCoordinator.Classify([root], [equalButDifferentInstance]));
    }
}
