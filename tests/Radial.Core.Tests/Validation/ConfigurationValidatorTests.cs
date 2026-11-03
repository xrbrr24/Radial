using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;
using Radial.Core.Validation;

namespace Radial.Core.Tests.Validation;

public class ConfigurationValidatorTests
{
    private static RadialConfiguration Standard => TestConfigs.Standard();

    private static IReadOnlyList<string> ErrorCodes(RadialConfiguration configuration) =>
        ConfigurationValidator.Validate(configuration).Errors.Select(e => e.Code).ToList();

    private static void AssertSingleError(RadialConfiguration configuration, string code)
    {
        Assert.Equal([code], ErrorCodes(configuration).ToArray());
    }

    private static RadialConfiguration WithMainItems(params RadialItem[] items) =>
        TestConfigs.WithMenu(Standard, TestConfigs.Menu(TestConfigs.Main, items));

    private static RadialConfiguration WithItem(RadialItem item) =>
        TestConfigs.WithMenu(Standard, TestConfigs.Menu(TestConfigs.Work, item));

    [Fact]
    public void StandardConfiguration_HasNoIssues()
    {
        Assert.Empty(ConfigurationValidator.Validate(Standard).Issues);
    }

    [Fact]
    public void EmptyConfiguration_IsValid()
    {
        Assert.True(ConfigurationValidator.Validate(RadialConfiguration.CreateEmpty()).IsValid);
    }

    [Fact]
    public void MenuWithEmptySlots_IsValid()
    {
        var configuration = WithMainItems(TestConfigs.Item("only", RadialDirection.South, new OpenFolderAction(@"C:\")));

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationValidator.Validate(null!));
    }

    // Identificadores

    [Fact]
    public void MissingRootMenu_IsReported()
    {
        AssertSingleError(Standard with { RootMenuId = new MenuId("nope") }, ValidationCodes.RootMissing);
    }

    [Fact]
    public void EmptyRootId_IsReported()
    {
        AssertSingleError(Standard with { RootMenuId = new MenuId(" ") }, ValidationCodes.RootEmpty);
    }

    [Fact]
    public void DefaultRootId_IsReportedWithoutThrowing()
    {
        Assert.Contains(ValidationCodes.RootEmpty, ErrorCodes(Standard with { RootMenuId = default }));
    }

    [Fact]
    public void EmptyMenuId_IsReported()
    {
        var configuration = Standard with { Menus = Standard.Menus.Append(TestConfigs.Menu(new MenuId(""))).ToList() };

        Assert.Contains(ValidationCodes.IdEmpty, ErrorCodes(configuration));
    }

    [Fact]
    public void DuplicateMenuIds_AreReported()
    {
        var configuration = Standard with { Menus = Standard.Menus.Append(TestConfigs.Menu(TestConfigs.Media)).ToList() };

        Assert.Contains(ValidationCodes.MenuDuplicateId, ErrorCodes(configuration));
    }

    [Fact]
    public void EmptyItemId_IsReported()
    {
        var configuration = WithItem(TestConfigs.Item(" ", RadialDirection.North, new OpenFolderAction(@"C:\")));

        AssertSingleError(configuration, ValidationCodes.IdEmpty);
    }

    [Fact]
    public void DuplicateItemIds_AreReported_EvenAcrossMenus()
    {
        var configuration = WithItem(TestConfigs.Item("browser", RadialDirection.North, new OpenFolderAction(@"C:\")));

        AssertSingleError(configuration, ValidationCodes.ItemDuplicateId);
    }

    [Fact]
    public void TooLongId_IsReported()
    {
        var item = TestConfigs.Item(new string('x', ConfigurationLimits.MaxIdLength + 1), RadialDirection.North, new OpenFolderAction(@"C:\")) with { Name = "ok" };
        var configuration = WithItem(item);

        AssertSingleError(configuration, ValidationCodes.IdTooLong);
    }

    [Fact]
    public void Identifiers_AreIndependentOfDisplayNames()
    {
        var renamed = TestConfigs.Standard();
        var main = renamed.Menus[0] with { Name = "Otro nombre" };

        Assert.True(ConfigurationValidator.Validate(TestConfigs.WithMenu(renamed, main)).IsValid);
    }

    // Nombres, posiciones, iconos

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyItemName_IsReported(string name)
    {
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Name = name };

        AssertSingleError(WithItem(item), ValidationCodes.NameEmpty);
    }

    [Fact]
    public void TooLongNames_AreReported()
    {
        var longName = new string('n', ConfigurationLimits.MaxNameLength + 1);
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Name = longName };
        var configuration = TestConfigs.WithMenu(Standard, TestConfigs.Menu(TestConfigs.Work, item) with { Name = longName });

        Assert.Equal(2, ErrorCodes(configuration).Count(c => c == ValidationCodes.NameTooLong));
    }

    [Fact]
    public void NameAtTheLimit_IsAccepted()
    {
        var name = new string('n', ConfigurationLimits.MaxNameLength);
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Name = name };

        Assert.True(ConfigurationValidator.Validate(WithItem(item)).IsValid);
    }

    [Fact]
    public void TwoItemsInTheSamePosition_AreReported()
    {
        var configuration = WithMainItems(
            TestConfigs.Item("a", RadialDirection.North, new OpenFolderAction(@"C:\")),
            TestConfigs.Item("b", RadialDirection.North, new OpenFolderAction(@"D:\")));

        AssertSingleError(configuration, ValidationCodes.ItemPositionDuplicate);
    }

    [Fact]
    public void UndefinedPosition_IsReported()
    {
        var configuration = WithItem(TestConfigs.Item("x", (RadialDirection)42, new OpenFolderAction(@"C:\")));

        AssertSingleError(configuration, ValidationCodes.ItemPositionInvalid);
    }

    [Fact]
    public void EightItemsFillingEveryPosition_AreValid()
    {
        var items = Enum.GetValues<RadialDirection>()
            .Select(d => TestConfigs.Item($"full-{d}", d, new OpenFolderAction(@"C:\")))
            .ToArray();

        Assert.True(ConfigurationValidator.Validate(WithMainItems(items)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void IconWithoutValue_IsReported(string value)
    {
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Icon = new IconReference(IconSource.Glyph, value) };

        AssertSingleError(WithItem(item), ValidationCodes.IconInvalid);
    }

    [Fact]
    public void IconWithUnknownSource_IsReported()
    {
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Icon = new IconReference((IconSource)9, "x") };

        AssertSingleError(WithItem(item), ValidationCodes.IconInvalid);
    }

    [Fact]
    public void ValidIcon_IsAccepted()
    {
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Icon = new IconReference(IconSource.File, @"C:\a.exe") };

        Assert.True(ConfigurationValidator.Validate(WithItem(item)).IsValid);
    }

    // Acciones

    [Fact]
    public void MissingAction_IsReportedWithoutThrowing()
    {
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Action = null! };

        AssertSingleError(WithItem(item), ValidationCodes.ActionMissing);
    }

    public static TheoryData<RadialAction> InvalidActions => new()
    {
        new LaunchApplicationAction(""),
        new LaunchApplicationAction("   "),
        new LaunchApplicationAction("a\0b"),
        new LaunchApplicationAction(@"C:\a.exe", Arguments: new string('a', RadialAction.MaxTextLength + 1)),
        new OpenFileAction(""),
        new OpenFolderAction(" "),
        new OpenUrlAction(""),
        new OpenUrlAction("example.com"),
        new OpenUrlAction("javascript:alert(1)"),
        new OpenUrlAction("file:///C:/Windows/System32/cmd.exe"),
        new OpenUrlAction("ftp://example.com/x"),
        new SendKeysAction(null!),
        new SendKeysAction(new KeyChord(KeyModifiers.Control, "")),
        new SendKeysAction(new KeyChord(KeyModifiers.Control, "Ctrl C")),
        new SendKeysAction(new KeyChord(KeyModifiers.Control, new string('k', 33))),
        new SendKeysAction(new KeyChord((KeyModifiers)64, "C")),
        new OpenMenuAction(new MenuId("")),
    };

    [Theory]
    [MemberData(nameof(InvalidActions))]
    public void ActionsWithMissingOrInvalidData_AreReported(RadialAction action)
    {
        var configuration = WithItem(TestConfigs.Item("x", RadialDirection.North, action));

        Assert.Contains(ValidationCodes.ActionInvalid, ErrorCodes(configuration));
    }

    public static TheoryData<RadialAction> ValidActions => new()
    {
        new LaunchApplicationAction(@"C:\Apps\a.exe"),
        new LaunchApplicationAction(@"C:\Apps\a.exe", "--flag", @"C:\Apps"),
        new OpenFileAction(@"C:\Docs\a.txt"),
        new OpenFolderAction(@"C:\Docs"),
        new OpenUrlAction("https://example.com/path?q=1"),
        new OpenUrlAction("HTTP://EXAMPLE.COM"),
        new OpenUrlAction("mailto:someone@example.com"),
        new SendKeysAction(new KeyChord(KeyModifiers.None, "F5")),
        new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Shift, "T")),
        new OpenMenuAction(TestConfigs.Media),
    };

    [Theory]
    [MemberData(nameof(ValidActions))]
    public void ActionsWithValidData_AreAccepted(RadialAction action)
    {
        var configuration = WithItem(TestConfigs.Item("x", RadialDirection.North, action));

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }

    // Submenús

    [Fact]
    public void ReferenceToMissingMenu_IsReported()
    {
        var configuration = WithItem(TestConfigs.Item("x", RadialDirection.North, new OpenMenuAction(new MenuId("ghost"))));

        AssertSingleError(configuration, ValidationCodes.ActionMenuMissing);
    }

    [Fact]
    public void MenuReferencingItself_IsACycle()
    {
        var configuration = WithItem(TestConfigs.Item("x", RadialDirection.North, new OpenMenuAction(TestConfigs.Work)));

        Assert.Contains(ValidationCodes.MenuCycle, ErrorCodes(configuration));
    }

    [Fact]
    public void TwoMenusReferencingEachOther_AreACycle()
    {
        var configuration = WithItem(TestConfigs.Item("x", RadialDirection.North, new OpenMenuAction(TestConfigs.Main)));

        var result = ConfigurationValidator.Validate(configuration);

        var cycle = Assert.Single(result.Errors, e => e.Code == ValidationCodes.MenuCycle);
        Assert.Contains("main", cycle.Message);
        Assert.Contains("work", cycle.Message);
    }

    [Fact]
    public void LongerCycle_IsDetected()
    {
        var configuration = TestConfigs.WithMenu(Standard, TestConfigs.Menu(
            TestConfigs.Deep,
            TestConfigs.Item("back", RadialDirection.North, new OpenMenuAction(TestConfigs.Main))));

        Assert.Contains(ValidationCodes.MenuCycle, ErrorCodes(configuration));
    }

    [Fact]
    public void CycleAmongUnreachableMenus_IsAlsoReported()
    {
        var a = TestConfigs.Menu(new MenuId("a"), TestConfigs.Item("a-to-b", RadialDirection.North, new OpenMenuAction(new MenuId("b"))));
        var b = TestConfigs.Menu(new MenuId("b"), TestConfigs.Item("b-to-a", RadialDirection.North, new OpenMenuAction(new MenuId("a"))));
        var configuration = Standard with { Menus = Standard.Menus.Append(a).Append(b).ToList() };

        Assert.Contains(ValidationCodes.MenuCycle, ErrorCodes(configuration));
    }

    [Fact]
    public void TwoItemsPointingToTheSameMenu_IsNotACycle()
    {
        var configuration = WithMainItems(
            TestConfigs.Item("m1", RadialDirection.North, new OpenMenuAction(TestConfigs.Media)),
            TestConfigs.Item("m2", RadialDirection.South, new OpenMenuAction(TestConfigs.Media)));

        Assert.Empty(ConfigurationValidator.Validate(configuration).Errors);
    }

    [Fact]
    public void DepthBeyondTheConfiguredMaximum_IsReported()
    {
        var configuration = Standard with { Navigation = Standard.Navigation with { MaxDepth = 2 } };

        AssertSingleError(configuration, ValidationCodes.MenuTooDeep);
    }

    [Fact]
    public void DepthEqualToTheMaximum_IsAccepted()
    {
        Assert.Equal(3, Standard.Navigation.MaxDepth);
        Assert.True(ConfigurationValidator.Validate(Standard).IsValid);
    }

    [Fact]
    public void MaxDepthOne_AllowsOnlyMenusWithoutGroups()
    {
        var configuration = RadialConfiguration.CreateEmpty() with { Navigation = new NavigationSettings { MaxDepth = 1 } };

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
        Assert.Contains(ValidationCodes.MenuTooDeep, ErrorCodes(Standard with { Navigation = Standard.Navigation with { MaxDepth = 1 } }));
    }

    [Fact]
    public void VeryLongChainOfMenus_DoesNotOverflowTheStack()
    {
        const int length = 50_000;
        var menus = new List<RadialMenu>();
        for (var i = 0; i < length; i++)
        {
            var next = i + 1 < length
                ? new[] { TestConfigs.Item($"i{i}", RadialDirection.North, new OpenMenuAction(new MenuId($"m{i + 1}"))) }
                : [];
            menus.Add(TestConfigs.Menu(new MenuId($"m{i}"), next));
        }

        var configuration = new RadialConfiguration { RootMenuId = new MenuId("m0"), Menus = menus };

        Assert.Contains(ValidationCodes.MenuTooDeep, ErrorCodes(configuration));
    }

    [Fact]
    public void UnreachableMenu_IsOnlyAWarning()
    {
        var orphan = TestConfigs.Menu(new MenuId("orphan"));
        var configuration = Standard with { Menus = Standard.Menus.Append(orphan).ToList() };

        var result = ConfigurationValidator.Validate(configuration);

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ValidationCodes.MenuUnreachable, warning.Code);
    }

    // Parámetros

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(ConfigurationLimits.MaxDeadZoneRadius + 1)]
    public void InvalidDeadZone_IsReported(double radius)
    {
        var configuration = Standard with { Geometry = Standard.Geometry with { DeadZoneRadius = radius } };

        AssertSingleError(configuration, ValidationCodes.GeometryDeadZoneInvalid);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(22.5)]
    [InlineData(45.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidHysteresis_IsReported(double degrees)
    {
        var configuration = Standard with { Geometry = Standard.Geometry with { HysteresisDegrees = degrees } };

        AssertSingleError(configuration, ValidationCodes.GeometryHysteresisInvalid);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(22.4)]
    public void ValidHysteresisBounds_AreAccepted(double degrees)
    {
        var configuration = Standard with { Geometry = Standard.Geometry with { HysteresisDegrees = degrees } };

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(49)]
    [InlineData(10_001)]
    public void InvalidSubmenuDwell_IsReported(int milliseconds)
    {
        var configuration = Standard with { Navigation = Standard.Navigation with { SubmenuDwell = TimeSpan.FromMilliseconds(milliseconds) } };

        AssertSingleError(configuration, ValidationCodes.NavigationSubmenuDwellInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void InvalidBackDwell_IsReported(int milliseconds)
    {
        var configuration = Standard with { Navigation = Standard.Navigation with { BackDwell = TimeSpan.FromMilliseconds(milliseconds) } };

        AssertSingleError(configuration, ValidationCodes.NavigationBackDwellInvalid);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(10_000)]
    public void DwellAtTheLimits_IsAccepted(int milliseconds)
    {
        var dwell = TimeSpan.FromMilliseconds(milliseconds);
        var configuration = Standard with { Navigation = Standard.Navigation with { SubmenuDwell = dwell, BackDwell = dwell } };

        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ConfigurationLimits.MaxDepthLimit + 1)]
    public void InvalidMaxDepth_IsReported(int depth)
    {
        var configuration = Standard with { Navigation = Standard.Navigation with { MaxDepth = depth } };

        Assert.Contains(ValidationCodes.NavigationMaxDepthInvalid, ErrorCodes(configuration));
    }

    [Fact]
    public void SeveralProblems_AreReportedTogether_WithPathsAndMessages()
    {
        var configuration = Standard with
        {
            RootMenuId = new MenuId("nope"),
            Geometry = new GeometrySettings { DeadZoneRadius = -1, HysteresisDegrees = 99 },
        };

        var result = ConfigurationValidator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count());
        Assert.All(result.Errors, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Message));
            Assert.False(string.IsNullOrWhiteSpace(e.Path));
        });
    }

    [Fact]
    public void ErrorPath_LocatesTheOffendingItem()
    {
        var item = TestConfigs.Item("bad-item", RadialDirection.North, new OpenFolderAction(""));

        var error = Assert.Single(ConfigurationValidator.Validate(WithItem(item)).Errors);

        Assert.Equal("menus[work].items[bad-item].action", error.Path);
    }

    [Fact]
    public void DefaultSettings_AreValid()
    {
        var geometry = new GeometrySettings();
        var navigation = new NavigationSettings();

        var configuration = RadialConfiguration.CreateEmpty();

        Assert.Equal(geometry, configuration.Geometry);
        Assert.Equal(navigation, configuration.Navigation);
        Assert.Equal(3, navigation.MaxDepth);
        Assert.True(ConfigurationValidator.Validate(configuration).IsValid);
    }
}
