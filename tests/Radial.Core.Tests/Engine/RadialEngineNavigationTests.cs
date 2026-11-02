using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;

namespace Radial.Core.Tests.Engine;

public class RadialEngineNavigationTests
{
    // Configuración estándar: permanencia de entrada 500 ms, de retroceso 400 ms, zona muerta 30.

    private static EngineHarness EnterWork()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Advance(500);
        Assert.Equal(["main", "work"], harness.StackIds());
        return harness;
    }

    [Fact]
    public void HoveringAGroup_StartsWithZeroProgress()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);

        Assert.Equal(RadialDirection.East, harness.Engine.SelectedDirection);
        Assert.Equal(0, harness.Engine.SubmenuProgress);
        Assert.Equal(["main"], harness.StackIds());
    }

    [Fact]
    public void SubmenuProgress_GrowsGraduallyBetweenZeroAndOne()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);

        harness.Advance(125);
        Assert.Equal(0.25, harness.Engine.SubmenuProgress, precision: 9);
        harness.Advance(125);
        Assert.Equal(0.5, harness.Engine.SubmenuProgress, precision: 9);
        harness.Advance(249);
        Assert.Equal(0.998, harness.Engine.SubmenuProgress, precision: 9);
        Assert.Equal(["main"], harness.StackIds());
    }

    [Fact]
    public void Submenu_OpensExactlyWhenConfiguredTimeIsReached()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);

        harness.Advance(499);
        Assert.Equal(["main"], harness.StackIds());

        harness.Advance(1);
        Assert.Equal(["main", "work"], harness.StackIds());
        Assert.Equal(TestConfigs.Work, harness.Engine.CurrentMenu!.Id);
    }

    [Fact]
    public void Submenu_CanOpenFromAPointerUpdate_WithoutAnExplicitTick()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Clock.AdvanceMilliseconds(500);

        harness.MoveTo(RadialDirection.East, distance: 120);

        Assert.Equal(["main", "work"], harness.StackIds());
    }

    [Fact]
    public void EnteringASubmenu_RecentersOnTheCurrentPointer_AndClearsState()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        var pointerAtEntry = harness.Engine.Pointer;

        harness.Advance(500);

        var engine = harness.Engine;
        Assert.Equal(pointerAtEntry, engine.Origin);
        Assert.NotEqual(EngineHarness.StartOrigin, engine.Origin);
        Assert.Null(engine.SelectedDirection);
        Assert.Null(engine.SelectedItem);
        Assert.Equal(0, engine.SubmenuProgress);
        Assert.Equal(0, engine.BackProgress);
        Assert.False(engine.IsBackArmed);
    }

    [Fact]
    public void AfterRecentering_DirectionsAreRelativeToTheNewOrigin()
    {
        var harness = EnterWork();
        var north = Pointers.At(harness.Engine.Origin, RadialDirection.North, 100);

        harness.Engine.UpdatePointer(north);

        // Respecto al origen antiguo ese punto estaría al noreste; respecto al nuevo está al norte.
        Assert.Equal(RadialDirection.NorthEast, RadialGeometry.Select(EngineHarness.StartOrigin, north, 30));
        Assert.Equal(RadialDirection.North, harness.Engine.SelectedDirection);
        Assert.Equal("notes", harness.Engine.SelectedItem!.Id.Value);
    }

    [Fact]
    public void SubmenuProgress_ResetsWhenLeavingTheGroupBeforeCompleting()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Advance(300);
        Assert.Equal(0.6, harness.Engine.SubmenuProgress, precision: 9);

        harness.MoveTo(RadialDirection.North);
        Assert.Equal(0, harness.Engine.SubmenuProgress);

        harness.MoveTo(RadialDirection.East).Advance(300);
        Assert.Equal(0.6, harness.Engine.SubmenuProgress, precision: 9);
        Assert.Equal(["main"], harness.StackIds());
    }

    [Fact]
    public void SubmenuProgress_ResetsWhenMovingToTheCenter()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Advance(400);

        harness.MoveToCenter();
        Assert.Equal(0, harness.Engine.SubmenuProgress);

        harness.MoveTo(RadialDirection.East).Advance(400);
        Assert.Equal(["main"], harness.StackIds());
    }

    [Fact]
    public void SubmenuProgress_RestartsWhenMovingBetweenTwoGroups()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Advance(400);

        harness.MoveTo(RadialDirection.SouthWest);
        Assert.Equal(0, harness.Engine.SubmenuProgress);

        harness.Advance(400);
        Assert.Equal(["main"], harness.StackIds());
        harness.Advance(100);
        Assert.Equal(["main", "media"], harness.StackIds());
    }

    [Fact]
    public void SubmenuProgress_DoesNotRestartWhileJitteringInsideTheSameSector()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Advance(250);

        harness.MoveTo(RadialDirection.East, distance: 140);
        harness.Advance(250);

        Assert.Equal(["main", "work"], harness.StackIds());
    }

    [Fact]
    public void ReleasingOverAGroupBeforeTheDwellCompletes_CancelsWithoutExecuting()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);
        harness.Advance(499);

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Cancelled, result.Kind);
        Assert.Equal(CancelReason.GroupNotEntered, result.Reason);
        Assert.Null(result.Item);
        Assert.False(harness.Engine.IsActive);
    }

    [Fact]
    public void ConfirmInsideASubmenu_ExecutesThatMenusItem()
    {
        var harness = EnterWork();
        harness.MoveTo(RadialDirection.North);

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Execute, result.Kind);
        Assert.Equal("notes", result.Item!.Id.Value);
        Assert.IsType<OpenFileAction>(result.Item.Action);
        Assert.Empty(harness.Engine.NavigationStack);
    }

    [Fact]
    public void ConfirmRightAfterEntering_CancelsBecauseThereIsNoSelection()
    {
        var harness = EnterWork();

        var result = harness.Engine.Confirm();

        Assert.Equal(CancelReason.NoSelection, result.Reason);
    }

    [Fact]
    public void BackNavigation_IsNotArmedRightAfterEntering_EvenAfterALongWait()
    {
        var harness = EnterWork();

        harness.Advance(10_000);

        Assert.Equal(["main", "work"], harness.StackIds());
        Assert.False(harness.Engine.IsBackArmed);
        Assert.Equal(0, harness.Engine.BackProgress);
    }

    [Fact]
    public void LeavingTheCenter_ArmsBackNavigation()
    {
        var harness = EnterWork();

        harness.MoveTo(RadialDirection.North);

        Assert.True(harness.Engine.IsBackArmed);
    }

    [Fact]
    public void BackNavigation_ReturnsToPreviousMenuExactlyWhenTimeIsReached()
    {
        var harness = EnterWork();
        harness.MoveTo(RadialDirection.North).MoveToCenter();

        harness.Advance(200);
        Assert.Equal(0.5, harness.Engine.BackProgress, precision: 9);
        harness.Advance(199);
        Assert.Equal(["main", "work"], harness.StackIds());

        harness.Advance(1);
        Assert.Equal(["main"], harness.StackIds());
        Assert.Equal(TestConfigs.Main, harness.Engine.CurrentMenu!.Id);
    }

    [Fact]
    public void BackNavigation_RecentersOnTheCurrentPointer_AndDisarms()
    {
        var harness = EnterWork();
        harness.MoveTo(RadialDirection.North).MoveToCenter();
        var pointer = harness.Engine.Pointer;

        harness.Advance(400);

        var engine = harness.Engine;
        Assert.Equal(pointer, engine.Origin);
        Assert.Null(engine.SelectedDirection);
        Assert.False(engine.IsBackArmed);
        Assert.Equal(0, engine.BackProgress);
    }

    [Fact]
    public void BackProgress_ResetsIfTheCursorLeavesTheCenterBeforeCompleting()
    {
        var harness = EnterWork();
        harness.MoveTo(RadialDirection.North).MoveToCenter();
        harness.Advance(300);
        Assert.Equal(0.75, harness.Engine.BackProgress, precision: 9);

        harness.MoveTo(RadialDirection.North);
        Assert.Equal(0, harness.Engine.BackProgress);

        harness.MoveToCenter().Advance(300);
        Assert.Equal(["main", "work"], harness.StackIds());
        Assert.Equal(0.75, harness.Engine.BackProgress, precision: 9);
    }

    [Fact]
    public void RootMenu_HasNoBackNavigation()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.North).MoveToCenter();

        harness.Advance(10_000);

        Assert.Equal(["main"], harness.StackIds());
        Assert.Equal(0, harness.Engine.BackProgress);
        Assert.True(harness.Engine.IsActive);
    }

    [Fact]
    public void ThreeLevels_CanBeEnteredAndLeftOneAtATime()
    {
        var harness = EnterWork();

        harness.MoveTo(RadialDirection.East).Advance(500);
        Assert.Equal(["main", "work", "deep"], harness.StackIds());

        // Volver a work: salir del centro, volver a él y esperar.
        harness.MoveTo(RadialDirection.North).MoveToCenter().Advance(400);
        Assert.Equal(["main", "work"], harness.StackIds());

        // Tras volver, la vuelta está desarmada: esperar en el centro no basta.
        harness.Advance(5_000);
        Assert.Equal(["main", "work"], harness.StackIds());

        harness.MoveTo(RadialDirection.North).MoveToCenter().Advance(400);
        Assert.Equal(["main"], harness.StackIds());
    }

    [Fact]
    public void ConfirmAtTheDeepestLevel_ExecutesItsItem()
    {
        var harness = EnterWork();
        harness.MoveTo(RadialDirection.East).Advance(500);
        harness.MoveTo(RadialDirection.North);

        var result = harness.Engine.Confirm();

        Assert.Equal("log", result.Item!.Id.Value);
    }

    [Fact]
    public void ReEnteringTheSameGroupAfterGoingBack_WorksAgain()
    {
        var harness = EnterWork();
        harness.MoveTo(RadialDirection.North).MoveToCenter().Advance(400);
        Assert.Equal(["main"], harness.StackIds());

        harness.MoveTo(RadialDirection.East).Advance(500);

        Assert.Equal(["main", "work"], harness.StackIds());
    }

    [Fact]
    public void Tick_WhileIdle_NeverEntersAnything()
    {
        var harness = new EngineHarness();

        harness.Advance(10_000);

        Assert.False(harness.Engine.IsActive);
    }

    [Fact]
    public void ZeroElapsedTime_NeverCompletesADwell()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East);

        for (var i = 0; i < 100; i++)
        {
            harness.Engine.Tick();
        }

        Assert.Equal(["main"], harness.StackIds());
    }
}
