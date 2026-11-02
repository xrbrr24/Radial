using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Engine;

public class RadialEngineLifecycleTests
{
    private static void AssertIdleAndClean(RadialEngine engine)
    {
        Assert.False(engine.IsActive);
        Assert.Null(engine.CurrentMenu);
        Assert.Empty(engine.NavigationStack);
        Assert.Null(engine.SelectedDirection);
        Assert.Null(engine.SelectedItem);
        Assert.Equal(0, engine.SubmenuProgress);
        Assert.Equal(0, engine.BackProgress);
        Assert.False(engine.IsBackArmed);
    }

    [Fact]
    public void NewEngine_IsIdle()
    {
        AssertIdleAndClean(new EngineHarness().Engine);
    }

    [Fact]
    public void Start_ActivatesRootMenuAtOrigin_WithoutSelection()
    {
        var harness = new EngineHarness().Start();
        var engine = harness.Engine;

        Assert.True(engine.IsActive);
        Assert.Equal(TestConfigs.Main, engine.CurrentMenu!.Id);
        Assert.Equal(["main"], harness.StackIds());
        Assert.Equal(EngineHarness.StartOrigin, engine.Origin);
        Assert.Equal(EngineHarness.StartOrigin, engine.Pointer);
        Assert.Null(engine.SelectedDirection);
        Assert.Null(engine.SelectedItem);
    }

    [Fact]
    public void UpdatePointer_InsideCenter_HasNoSelection()
    {
        var harness = new EngineHarness().Start();

        harness.MoveTo(RadialDirection.North, distance: 29);

        Assert.Null(harness.Engine.SelectedDirection);
    }

    [Theory]
    [InlineData(RadialDirection.North, "browser")]
    [InlineData(RadialDirection.NorthEast, "docs")]
    [InlineData(RadialDirection.East, "work")]
    [InlineData(RadialDirection.South, "folder")]
    [InlineData(RadialDirection.West, "copy")]
    [InlineData(RadialDirection.SouthWest, "media")]
    public void UpdatePointer_OutsideCenter_SelectsTheItemInThatDirection(RadialDirection direction, string expectedItem)
    {
        var harness = new EngineHarness().Start();

        harness.MoveTo(direction);

        Assert.Equal(direction, harness.Engine.SelectedDirection);
        Assert.Equal(expectedItem, harness.Engine.SelectedItem!.Id.Value);
    }

    [Theory]
    [InlineData(RadialDirection.SouthEast)]
    [InlineData(RadialDirection.NorthWest)]
    public void UpdatePointer_OverEmptySlot_SelectsDirectionButNoItem(RadialDirection direction)
    {
        var harness = new EngineHarness().Start();

        harness.MoveTo(direction);

        Assert.Equal(direction, harness.Engine.SelectedDirection);
        Assert.Null(harness.Engine.SelectedItem);
    }

    [Fact]
    public void UpdatePointer_UsesHysteresisFromConfiguration()
    {
        var harness = new EngineHarness().Start();
        harness.Engine.UpdatePointer(Pointers.AtAngle(EngineHarness.StartOrigin, 10, 100));

        harness.Engine.UpdatePointer(Pointers.AtAngle(EngineHarness.StartOrigin, 25, 100));

        Assert.Equal(RadialDirection.North, harness.Engine.SelectedDirection);
    }

    [Fact]
    public void Confirm_OnExecutableItem_ReturnsItAndGoesIdle()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.North);

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Execute, result.Kind);
        Assert.Equal("browser", result.Item!.Id.Value);
        Assert.IsType<LaunchApplicationAction>(result.Item.Action);
        AssertIdleAndClean(harness.Engine);
    }

    [Fact]
    public void Confirm_WithoutSelection_CancelsWithoutExecuting()
    {
        var harness = new EngineHarness().Start();

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Cancelled, result.Kind);
        Assert.Equal(CancelReason.NoSelection, result.Reason);
        Assert.Null(result.Item);
        AssertIdleAndClean(harness.Engine);
    }

    [Fact]
    public void Confirm_OnEmptySlot_CancelsWithoutExecuting()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.NorthWest);

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Cancelled, result.Kind);
        Assert.Equal(CancelReason.EmptySlot, result.Reason);
        Assert.Null(result.Item);
        AssertIdleAndClean(harness.Engine);
    }

    [Fact]
    public void Cancel_ClearsAllInteractionState()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East).Advance(250);
        Assert.True(harness.Engine.SubmenuProgress > 0);

        harness.Engine.Cancel();

        AssertIdleAndClean(harness.Engine);
    }

    [Fact]
    public void Cancel_FromInsideASubmenu_AbandonsTheWholeStack()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East).Advance(500);
        Assert.Equal(["main", "work"], harness.StackIds());

        harness.Engine.Cancel();

        AssertIdleAndClean(harness.Engine);
    }

    [Fact]
    public void Start_WhileActive_RestartsTheInteractionAtRoot()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East).Advance(500);
        Assert.Equal(2, harness.Engine.NavigationStack.Count);
        var newOrigin = new PointD(10, 20);

        harness.Engine.Start(newOrigin);

        Assert.Equal(["main"], harness.StackIds());
        Assert.Equal(newOrigin, harness.Engine.Origin);
        Assert.Null(harness.Engine.SelectedDirection);
        Assert.Equal(0, harness.Engine.SubmenuProgress);
        Assert.False(harness.Engine.IsBackArmed);
    }

    [Fact]
    public void WhenIdle_PointerTickAndCancelAreIgnored()
    {
        var harness = new EngineHarness();

        harness.Engine.UpdatePointer(new PointD(800, 800));
        harness.Engine.Tick();
        harness.Engine.Cancel();

        AssertIdleAndClean(harness.Engine);
        Assert.Equal(default, harness.Engine.Pointer);
    }

    [Fact]
    public void WhenIdle_ConfirmReturnsInactive_AndExecutesNothing()
    {
        var result = new EngineHarness().Engine.Confirm();

        Assert.Equal(RadialResultKind.NotActive, result.Kind);
        Assert.Null(result.Item);
    }

    [Fact]
    public void AfterConfirm_AStaleSecondConfirmOrMoveDoesNothing()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.North);
        harness.Engine.Confirm();

        var second = harness.Engine.Confirm();
        harness.Engine.UpdatePointer(Pointers.At(EngineHarness.StartOrigin, RadialDirection.South, 100));

        Assert.Equal(RadialResultKind.NotActive, second.Kind);
        AssertIdleAndClean(harness.Engine);
    }

    [Fact]
    public void AfterCancel_PointerUpdatesDoNotLeakIntoANewInteraction()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.South);
        harness.Engine.Cancel();
        harness.Engine.UpdatePointer(new PointD(900, 900));

        harness.Engine.Start(EngineHarness.StartOrigin);

        Assert.Equal(EngineHarness.StartOrigin, harness.Engine.Pointer);
        Assert.Null(harness.Engine.SelectedDirection);
    }

    [Fact]
    public void ANewInteraction_AfterConfirm_StartsFromScratch()
    {
        var harness = new EngineHarness().Start().MoveTo(RadialDirection.East).Advance(500);
        harness.Engine.Cancel();

        harness.Start();

        Assert.Equal(["main"], harness.StackIds());
        Assert.Null(harness.Engine.SelectedDirection);
        Assert.Equal(0, harness.Engine.SubmenuProgress);
    }

    [Fact]
    public void NonFinitePositions_AreRejected()
    {
        var harness = new EngineHarness();

        Assert.Throws<ArgumentOutOfRangeException>(() => harness.Engine.Start(new PointD(double.NaN, 0)));
        harness.Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => harness.Engine.UpdatePointer(new PointD(0, double.PositiveInfinity)));
        Assert.True(harness.Engine.IsActive);
    }

    [Fact]
    public void Constructor_RejectsInvalidConfiguration()
    {
        var invalid = TestConfigs.Standard() with { RootMenuId = new MenuId("nope") };

        var exception = Assert.Throws<InvalidConfigurationException>(() => new RadialEngine(invalid, new ManualClock()));

        Assert.Contains(exception.Result.Errors, e => e.Code == ValidationCodes.RootMissing);
    }

    [Fact]
    public void Engine_WorksWithAnEmptyRootMenu()
    {
        var harness = new EngineHarness(RadialConfiguration.CreateEmpty()).Start().MoveTo(RadialDirection.North);

        var result = harness.Engine.Confirm();

        Assert.Equal(CancelReason.EmptySlot, result.Reason);
    }
}
