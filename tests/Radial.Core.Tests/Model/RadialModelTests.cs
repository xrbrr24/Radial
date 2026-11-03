using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Tests.Support;

namespace Radial.Core.Tests.Model;

public class RadialModelTests
{
    [Fact]
    public void Menu_ReturnsItemsByPosition_AndNullForEmptySlots()
    {
        var main = TestConfigs.Standard().Menus[0];

        Assert.Equal("browser", main.GetItem(RadialDirection.North)!.Id.Value);
        Assert.Null(main.GetItem(RadialDirection.SouthEast));
        Assert.Null(main.GetItem(RadialDirection.NorthWest));
    }

    [Fact]
    public void Item_IsAGroup_OnlyWhenItOpensAMenu()
    {
        var main = TestConfigs.Standard().Menus[0];

        Assert.True(main.GetItem(RadialDirection.East)!.IsGroup);
        Assert.False(main.GetItem(RadialDirection.North)!.IsGroup);
    }

    [Fact]
    public void Identifiers_AreComparedByValue_NotByDisplayName()
    {
        var first = TestConfigs.Item("same", RadialDirection.North, new OpenFolderAction(@"C:\")) with { Name = "A" };
        var second = TestConfigs.Item("same", RadialDirection.South, new OpenFolderAction(@"D:\")) with { Name = "B" };

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(new ItemId("a"), new ItemId("A"));
    }

    [Fact]
    public void TryGetMenu_FindsMenusById()
    {
        var configuration = TestConfigs.Standard();

        Assert.True(configuration.TryGetMenu(TestConfigs.Deep, out var deep));
        Assert.Equal(TestConfigs.Deep, deep.Id);
        Assert.False(configuration.TryGetMenu(new MenuId("ghost"), out _));
    }

    [Fact]
    public void EveryActionKind_CanBeRepresented()
    {
        RadialAction[] actions =
        [
            new LaunchApplicationAction(@"C:\a.exe", "--x", @"C:\"),
            new OpenFileAction(@"C:\a.txt"),
            new OpenFolderAction(@"C:\"),
            new OpenUrlAction("https://example.com"),
            new SendKeysAction(new KeyChord(KeyModifiers.Control | KeyModifiers.Alt, "ArrowLeft")),
            new OpenMenuAction(TestConfigs.Work),
        ];

        Assert.All(actions, action => Assert.Empty(action.GetProblems()));
    }

    [Fact]
    public void Records_SupportNonDestructiveCopies()
    {
        var item = TestConfigs.Item("x", RadialDirection.North, new OpenFolderAction(@"C:\"));

        var moved = item with { Position = RadialDirection.South };

        Assert.Equal(RadialDirection.North, item.Position);
        Assert.Equal(RadialDirection.South, moved.Position);
    }
}
