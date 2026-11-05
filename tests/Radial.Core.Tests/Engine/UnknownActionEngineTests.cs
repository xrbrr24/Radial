using Radial.Core.Engine;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Engine;

/// <summary>Una acción de tipo desconocido se conserva y se valida con un aviso, pero nunca se devuelve como ejecutable.</summary>
public class UnknownActionEngineTests
{
    private static readonly UnknownAction Unknown = new("futureAction", "{\"type\":\"futureAction\",\"x\":{\"y\":[1,2]}}");

    private static RadialConfiguration WithUnknownEast() => new()
    {
        RootMenuId = TestConfigs.Main,
        Menus =
        [
            TestConfigs.Menu(
                TestConfigs.Main,
                TestConfigs.Item("known", RadialDirection.North, new OpenFolderAction(@"C:\Temp")),
                TestConfigs.Item("future", RadialDirection.East, Unknown)),
        ],
    };

    [Fact]
    public void ConfirmingAnUnknownAction_DoesNotExecuteIt()
    {
        var harness = new EngineHarness(WithUnknownEast()).Start().MoveTo(RadialDirection.East);

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Cancelled, result.Kind);
        Assert.Equal(CancelReason.UnsupportedAction, result.Reason);
        Assert.Null(result.Item);
        Assert.False(harness.Engine.IsActive);
    }

    [Fact]
    public void ConfirmingAKnownAction_NextToAnUnknownOne_StillExecutes()
    {
        var harness = new EngineHarness(WithUnknownEast()).Start().MoveTo(RadialDirection.North);

        var result = harness.Engine.Confirm();

        Assert.Equal(RadialResultKind.Execute, result.Kind);
        Assert.Equal(new ItemId("known"), result.Item!.Id);
    }

    [Fact]
    public void AnUnknownAction_IsMarkedAsUnsupported_ButIsNotAGroup()
    {
        var item = TestConfigs.Item("future", RadialDirection.East, Unknown);

        Assert.True(item.IsUnsupported);
        Assert.False(item.IsGroup);
    }

    [Fact]
    public void AnUnknownAction_ProducesAWarning_AndTheConfigurationStaysValid()
    {
        var result = ConfigurationValidator.Validate(WithUnknownEast());

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings, w => w.Code == ValidationCodes.ActionUnknownType);
        Assert.Contains("future", warning.Path);
    }

    [Fact]
    public void AnUnknownActionWithoutAType_IsAValidationError()
    {
        var configuration = new RadialConfiguration
        {
            RootMenuId = TestConfigs.Main,
            Menus = [TestConfigs.Menu(TestConfigs.Main, TestConfigs.Item("future", RadialDirection.East, new UnknownAction(" ", "{}")))],
        };

        var result = ConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.ActionInvalid);
    }
}
